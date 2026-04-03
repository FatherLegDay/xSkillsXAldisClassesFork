using CombatOverhaul.Implementations;
using HarmonyLib;
using System;
using Vintagestory.API.Common;

namespace XSkills
{
    public class ItemStackMeleeWeaponStatsPatch : ManualPatch
    {
        public static void Apply(Harmony harmony, Type type, XSkills xSkills)
        {
            PatchMethod(harmony, type, typeof(ItemStackMeleeWeaponStatsPatch), "FromItemStack");
        }

        [HarmonyPostfix]
        [HarmonyPatch("FromItemStack")]
        public static void FromItemStackPostfix(ref ItemStackMeleeWeaponStats __result, ItemStack stack)
        {
            float quality = stack?.Attributes.TryGetFloat("quality") ?? 0.0f;
            if (quality > 0.0f)
            {
                bool isWeapon =
                    __result.DamageMultiplier > 0f ||
                    __result.DamageBonus > 0f ||
                    __result.ThrownDamageMultiplier > 0f;

                float damageMul = QualityUtil.GetDamageMultiplier(quality);
                __result = new ItemStackMeleeWeaponStats(
                    __result.DamageMultiplier * damageMul,
                    __result.DamageBonus,
                    __result.DamageTierBonus,
                    __result.AttackSpeed,
                    __result.BlockTierBonus,
                    __result.ParryTierBonus,
                    __result.ThrownDamageMultiplier * damageMul,
                    __result.ThrownDamageTierBonus,
                    __result.ThrownAimingDifficulty * (1.0f - quality * 0.01f),
                    __result.ThrownProjectileSpeedMultiplier,
                    __result.KnockbackMultiplier * (1.0f + quality * 0.01f),
                    (int)(__result.ArmorPiercingBonus * (1.0f + quality * 0.01f))
                    );
                if (quality > 0f)
                {
                    stack.Attributes.SetBool("xskills-hasdamage", true);
                }
            }
        }
    }

    public class ItemStackRangedStatsPatch : ManualPatch
    {
        public static void Apply(Harmony harmony, Type type, XSkills xSkills)
        {
            PatchMethod(harmony, type, typeof(ItemStackRangedStatsPatch), "FromItemStack");
        }

        [HarmonyPostfix]
        [HarmonyPatch("FromItemStack")]
        public static void FromItemStackPostfix(ref ItemStackRangedStats __result, ItemStack stack)
        {
            float quality = stack?.Attributes.TryGetFloat("quality") ?? 0.0f;
            if (quality > 0.0f)
            {
                float damageMul = QualityUtil.GetDamageMultiplier(quality);
                __result = new ItemStackRangedStats(
                    __result.ReloadSpeed * (1.0f + quality * 0.01f),
                    __result.DamageMultiplier * damageMul,
                    __result.DamageTierBonus,
                    __result.ProjectileSpeed,
                    __result.DispersionMultiplier,
                    __result.AimingDifficulty * (1.0f - quality * 0.01f)
                    );
            }
        }
    }
}
