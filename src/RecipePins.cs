using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimNewUI;

// Attached to the item interface: one character list and one inventory-only snapshot feed both views.
internal sealed class RecipePins
{
    private const string SaveKey = "valheimnewui_recipe_pins";
    private readonly List<string> ids = new();
    private readonly List<Recipe> recipes = new();
    private readonly List<RecipePinCost> costs = new();
    private readonly Dictionary<string, ItemDrop.ItemData> materials = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, int Quality), int> stock = new();
    private readonly List<(string Name, int Quality)> stockKeys = new();
    private Player owner;
    private ObjectDB database;
    private int databaseCount = -1;
    private float nextRead;
    private bool dirty;

    internal IReadOnlyList<string> Ids => ids;
    internal RecipePinPlan Plan { get; private set; }
    internal int Revision { get; private set; }
    internal int Session { get; private set; }

    internal static bool CanEdit(InventoryGui gui) => gui && InventoryGui.IsVisible() &&
        !Menu.IsVisible() && !Console.IsVisible() && !(gui.m_variantDialog && gui.m_variantDialog.gameObject.activeSelf)
        && !(gui.m_splitDialog && gui.m_splitDialog.IsActive);

    internal static bool CanPin(Recipe recipe) => recipe && recipe.m_item && recipe.m_enabled && !recipe.m_noCraftOnlyUpgrade;

    internal static string Id(Recipe recipe) => Uri.EscapeDataString(recipe.name) + ":" + Uri.EscapeDataString(recipe.m_item.name);

    internal bool Contains(string id) => ids.Contains(id);

    internal ItemDrop.ItemData Result(int index) => recipes[index] ? recipes[index].m_item.m_itemData : null;
    internal bool HasAlternatives(int index) => costs[index]?.AnyIngredient == true;
    internal ItemDrop.ItemData Material(string name) => materials.TryGetValue(name, out var item) ? item : null;

    internal void Refresh(Player player)
    {
        if (owner != player)
        {
            owner = player;
            ids.Clear();
            if (player && player.TryGetUniqueKeyValue(SaveKey, out var saved))
                foreach (string id in saved.Split('|'))
                    if (id.IndexOf(':') > 0 && !ids.Contains(id)) ids.Add(id);
            Session++;
            dirty = true;
        }
        var currentDb = ObjectDB.instance;
        int count = currentDb ? currentDb.m_recipes.Count : 0;
        if (database != currentDb || databaseCount != count)
        {
            database = currentDb;
            databaseCount = count;
            dirty = true;
        }
        if (!dirty && (ids.Count == 0 || Time.unscaledTime < nextRead)) return;
        nextRead = Time.unscaledTime + 0.15f;
        if (dirty) Resolve();
        bool changed = dirty;
        var inventory = owner ? owner.GetInventory() : null;
        foreach (var key in stockKeys)
        {
            int have = inventory?.CountItems(key.Name, key.Quality, true) ?? 0;
            if (have == stock[key]) continue;
            stock[key] = have;
            changed = true;
        }
        dirty = false;
        if (!changed) return;
        Plan = RecipePinPlan.Calculate(costs, (name, quality) => stock[(name, quality)]);
        Revision++;
    }

    internal void Toggle(Recipe recipe)
    {
        if (!CanPin(recipe) || !CanChange()) return;
        string id = Id(recipe);
        if (!ids.Remove(id)) ids.Add(id);
        Save();
    }

    internal void Remove(string id)
    {
        if (!CanChange() || !ids.Remove(id)) return;
        Save();
    }

    private bool CanChange() => owner && owner == Player.m_localPlayer && !owner.IsDead();

    private void Save()
    {
        // The native character save writes this value with the rest of the player's unique keys.
        if (ids.Count == 0) owner.RemoveUniqueKeyValue(SaveKey);
        else owner.AddUniqueKeyValue(SaveKey, string.Join("|", ids));
        dirty = true;
    }

    private void Resolve()
    {
        recipes.Clear();
        costs.Clear();
        stock.Clear();
        stockKeys.Clear();
        materials.Clear();
        if (ids.Count == 0) return;
        var byId = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        if (database)
            foreach (var recipe in database.m_recipes)
            {
                if (!CanPin(recipe)) continue;
                string id = Id(recipe);
                if (ids.Contains(id) && !byId.ContainsKey(id)) byId.Add(id, recipe);
            }
        foreach (string id in ids)
        {
            byId.TryGetValue(id, out var recipe);
            recipes.Add(recipe);
            costs.Add(BuildCost(recipe));
        }
    }

    private RecipePinCost BuildCost(Recipe recipe)
    {
        if (!recipe) return null;
        var costs = new List<PinIngredient>();
        bool upgrader = recipe.m_craftingStation && recipe.m_craftingStation.m_upgrader;
        foreach (var requirement in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
        {
            if (requirement == null || !requirement.m_resItem || requirement.m_upgraderResource != upgrader) continue;
            var item = requirement.m_resItem.m_itemData;
            string name = item.m_shared.m_name;
            int amount = requirement.GetAmount(1);
            if (amount <= 0 || string.IsNullOrEmpty(name)) continue;
            int maxQuality = Mathf.Max(1, item.m_shared.m_maxQuality);
            costs.Add(new PinIngredient(name, amount, maxQuality));
            materials[name] = item;
            for (int quality = 1; quality <= maxQuality; quality++)
                if (stock.TryAdd((name, quality), 0)) stockKeys.Add((name, quality));
        }
        return new RecipePinCost(recipe.m_requireOnlyOneIngredient, costs.ToArray());
    }
}
