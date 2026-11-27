using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimNewUI;

internal readonly struct PinIngredient
{
    internal readonly string Name;
    internal readonly int Amount, MaxQuality;
    internal PinIngredient(string name, int amount, int maxQuality = 1) =>
        (Name, Amount, MaxQuality) = (name, amount, maxQuality);
}

internal sealed class RecipePinCost
{
    internal readonly bool AnyIngredient;
    internal readonly PinIngredient[] Ingredients;
    internal RecipePinCost(bool anyIngredient, PinIngredient[] ingredients) =>
        (AnyIngredient, Ingredients) = (anyIngredient, ingredients);
}

internal readonly struct PinMaterial
{
    internal readonly string Name;
    internal readonly int Have, Need;
    internal bool Ready => Have >= Need;
    internal PinMaterial(string name, int have, int need) => (Name, Have, Need) = (name, have, need);
}

internal sealed class RecipePinPlan
{
    internal readonly struct RecipeMaterials
    {
        internal readonly PinMaterial[] Materials;
        internal readonly bool Ready;
        internal RecipeMaterials(PinMaterial[] materials, bool ready) => (Materials, Ready) = (materials, ready);
    }

    internal readonly RecipeMaterials[] Recipes;
    internal readonly PinMaterial[] Total;
    internal readonly bool Ready;

    private RecipePinPlan(RecipeMaterials[] recipes, PinMaterial[] total, bool ready) =>
        (Recipes, Total, Ready) = (recipes, total, ready);

    internal static RecipePinPlan Calculate(IReadOnlyList<RecipePinCost> costs, Func<string, int, int> count)
    {
        var supplies = new Dictionary<string, Supply>(StringComparer.Ordinal);
        foreach (var cost in costs)
        {
            if (cost == null) continue;
            foreach (var ingredient in cost.Ingredients)
            {
                if (!supplies.TryGetValue(ingredient.Name, out var supply))
                    supplies.Add(ingredient.Name, supply = new Supply(ingredient.Name));
                supply.MaxQuality = Math.Max(supply.MaxQuality, ingredient.MaxQuality);
            }
        }
        foreach (var supply in supplies.Values) supply.Read(count);

        var recipes = new RecipeMaterials[costs.Count];
        var choices = new List<Allocation[]>();
        int missing = 0;
        for (int i = 0; i < costs.Count; i++)
        {
            var cost = costs[i];
            if (cost == null)
            {
                recipes[i] = new RecipeMaterials(Array.Empty<PinMaterial>(), false);
                continue;
            }
            var ingredients = cost.AnyIngredient ? cost.Ingredients : cost.Ingredients
                .GroupBy(x => x.Name, StringComparer.Ordinal)
                .Select(group => new PinIngredient(group.Key, group.Sum(x => x.Amount), group.Min(x => x.MaxQuality))).ToArray();
            var materials = new PinMaterial[ingredients.Length];
            var alternatives = cost.AnyIngredient ? new List<Allocation>() : null;
            for (int j = 0; j < ingredients.Length; j++)
            {
                var ingredient = ingredients[j];
                var supply = supplies[ingredient.Name];
                int most = 0;
                for (int q = 0; q < ingredient.MaxQuality; q++) most = Math.Max(most, supply.Have[q]);
                materials[j] = new PinMaterial(ingredient.Name, most, ingredient.Amount);
                if (cost.AnyIngredient) AddQualities(alternatives, ingredient, supply);
                else if (ingredient.MaxQuality == 1) missing += new Allocation(supply, 0, ingredient.Amount).Add();
                else
                {
                    var qualityOptions = new List<Allocation>();
                    AddQualities(qualityOptions, ingredient, supply);
                    choices.Add(qualityOptions.ToArray());
                }
            }
            if (alternatives?.Count > 0) choices.Add(alternatives.ToArray());
            recipes[i] = new RecipeMaterials(materials,
                cost.AnyIngredient ? materials.Any(x => x.Ready) : materials.All(x => x.Ready));
        }

        // Reserve fixed inputs first, then find a plan for recipes with alternate materials or
        // qualities. A batch uses a single quality, but separate batches can use different ones.
        var best = new Allocation[choices.Count];
        var path = new Allocation[choices.Count];
        int bestMissing = int.MaxValue;
        int budget = 16384; // Keep pathological modded recipe sets from stalling the render thread.
        Search(0, missing);
        foreach (var input in best) input.Add();
        var total = supplies.Values.Where(supply => supply.Need.Sum() > 0).Select(supply => supply.Summary()).ToArray();
        return new RecipePinPlan(recipes, total, recipes.All(x => x.Ready) && total.All(x => x.Ready));

        void Search(int index, int deficit)
        {
            if (deficit >= bestMissing || (budget-- <= 0 && bestMissing != int.MaxValue)) return;
            if (index == choices.Count)
            {
                Array.Copy(path, best, path.Length);
                bestMissing = deficit;
                return;
            }
            // Try the least costly current option first; recipe/quality order breaks ties.
            foreach (var input in choices[index].OrderBy(x => x.Shortage))
            {
                path[index] = input;
                Search(index + 1, deficit + input.Add());
                input.Remove();
                if (bestMissing == missing || budget <= 0) break;
            }
        }
    }

    private static void AddQualities(List<Allocation> options, PinIngredient ingredient, Supply supply)
    {
        for (int q = 0; q < ingredient.MaxQuality; q++)
            options.Add(new Allocation(supply, q, ingredient.Amount));
    }

    private sealed class Supply
    {
        internal readonly string Name;
        internal int MaxQuality;
        internal int[] Have, Need;
        internal Supply(string name) => Name = name;
        internal void Read(Func<string, int, int> count)
        {
            Have = new int[MaxQuality];
            Need = new int[MaxQuality];
            for (int i = 0; i < Have.Length; i++) Have[i] = count(Name, i + 1);
        }
        internal PinMaterial Summary()
        {
            int requested = 0, used = 0;
            for (int i = 0; i < Need.Length; i++)
            {
                requested += Need[i];
                used += Math.Min(Have[i], Need[i]);
            }
            return new PinMaterial(Name, used < requested ? used : Have.Sum(), requested);
        }
    }

    private readonly struct Allocation
    {
        private readonly Supply supply;
        private readonly int index, amount;
        internal Allocation(Supply supply, int index, int amount) =>
            (this.supply, this.index, this.amount) = (supply, index, amount);
        internal int Shortage => amount - Math.Min(amount, Math.Max(0, supply.Have[index] - supply.Need[index]));
        internal int Add()
        {
            int shortage = Shortage;
            supply.Need[index] += amount;
            return shortage;
        }
        internal void Remove() => supply.Need[index] -= amount;
    }
}
