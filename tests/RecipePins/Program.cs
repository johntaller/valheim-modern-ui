using System;
using System.Linq;
using ValheimNewUI;

var tests = new (string Name, Action Run)[]
{
    (nameof(SharedMaterialsAreCountedOnce), SharedMaterialsAreCountedOnce),
    (nameof(AlternativeDoesNotDoubleCountOrStealFixedStock), AlternativeDoesNotDoubleCountOrStealFixedStock),
    (nameof(AlternativeAllocationCanBacktrack), AlternativeAllocationCanBacktrack),
    (nameof(OneCraftCannotMixQualities), OneCraftCannotMixQualities),
    (nameof(MultipleCraftsCanUseDifferentQualities), MultipleCraftsCanUseDifferentQualities),
    (nameof(UnavailableRecipeIsNotMaterialReady), UnavailableRecipeIsNotMaterialReady)
};
foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

static void SharedMaterialsAreCountedOnce()
{
    var result = RecipePinPlan.Calculate(new[]
    {
        new RecipePinCost(false, new[] { new PinIngredient("wood", 3), new PinIngredient("wood", 2) }),
        new RecipePinCost(false, new[] { new PinIngredient("wood", 4), new PinIngredient("stone", 1) })
    }, (name, _) => name == "wood" ? 6 : 2);
    CheckMaterial(result.Recipes[0].Materials.Single(), "wood", 6, 5);
    CheckMaterial(result.Recipes[1].Materials[0], "wood", 6, 4);
    CheckMaterial(result.Total[0], "wood", 6, 9);
    CheckMaterial(result.Total[1], "stone", 2, 1);
    Check(result.Recipes.All(x => x.Ready), "each recipe must be ready independently");
    Check(!result.Ready, "the same stock cannot cover both pins at once");
}

static void AlternativeDoesNotDoubleCountOrStealFixedStock()
{
    var plan = RecipePinPlan.Calculate(new[]
    {
        new RecipePinCost(true, new[] { new PinIngredient("fish", 2), new PinIngredient("meat", 2) }),
        new RecipePinCost(false, new[] { new PinIngredient("fish", 2) })
    }, (_, _) => 2);
    Check(plan.Ready, "a viable alternative leaves the fixed ingredient available");
    CheckMaterial(plan.Total.Single(x => x.Name == "fish"), "fish", 2, 2);
    CheckMaterial(plan.Total.Single(x => x.Name == "meat"), "meat", 2, 2);
    Check(plan.Recipes[0].Materials.Length == 2 && plan.Recipes[0].Ready, "individual pins show available alternatives");
}

static void AlternativeAllocationCanBacktrack()
{
    var plan = RecipePinPlan.Calculate(new[]
    {
        new RecipePinCost(true, new[] { new PinIngredient("fish", 2), new PinIngredient("meat", 2) }),
        new RecipePinCost(true, new[] { new PinIngredient("fish", 2), new PinIngredient("berries", 2) })
    }, (name, _) => name == "berries" ? 0 : 2);
    Check(plan.Ready, "different alternatives can fully cover the two pins");
    CheckMaterial(plan.Total.Single(x => x.Name == "meat"), "meat", 2, 2);
}

static void OneCraftCannotMixQualities()
{
    var plan = RecipePinPlan.Calculate(new[]
    {
        new RecipePinCost(true, new[] { new PinIngredient("fish", 3, 2) })
    }, (_, _) => 2);
    Check(!plan.Ready && !plan.Recipes[0].Ready, "two partial qualities can't be pooled to craft once");
    CheckMaterial(plan.Recipes[0].Materials.Single(), "fish", 2, 3);
    CheckMaterial(plan.Total.Single(), "fish", 2, 3);
}

static void MultipleCraftsCanUseDifferentQualities()
{
    var cost = new RecipePinCost(true, new[] { new PinIngredient("fish", 2, 2) });
    var plan = RecipePinPlan.Calculate(new[] { cost, cost }, (_, _) => 2);
    Check(plan.Ready, "separate crafts can use separate qualities");
    CheckMaterial(plan.Total.Single(), "fish", 4, 4);
}

static void UnavailableRecipeIsNotMaterialReady()
{
    var plan = RecipePinPlan.Calculate(new RecipePinCost[] { null }, (_, _) => 20);
    Check(!plan.Ready && !plan.Recipes[0].Ready, "an unresolved saved recipe must not appear complete");
}


static void CheckMaterial(PinMaterial actual, string name, int have, int need) =>
    Check(actual.Name == name && actual.Have == have && actual.Need == need,
        $"expected {name} {have}/{need}, got {actual.Name} {actual.Have}/{actual.Need}");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
