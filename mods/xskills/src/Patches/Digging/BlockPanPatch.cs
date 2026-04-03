using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using XLib.XLeveling;

namespace XSkills
{
    [HarmonyPatch(typeof(BlockPan))]
    public class BlockPanPatch
    {
        /// <summary>
        /// Prepares the Harmony patch.
        /// Only patches the methods if necessary.
        /// </summary>
        /// <param name="original">The method to be patched.</param>
        /// <returns>whether the method should be patched.</returns>
        public static bool Prepare(MethodBase original)
        {
            XSkills xSkills = XSkills.Instance;
            if (xSkills == null) return false;
            xSkills.Skills.TryGetValue("digging", out Skill skill);
            Digging digging = skill as Digging;

            if (!(digging?.Enabled ?? false)) return false;

            // Always patch the type so we can always add XP on CreateDrop.
            if (original == null) return true;

            switch (original.Name)
            {
                case "OnHeldInteractStep":
                case "OnHeldInteractStop":
                    return digging[digging.QuickPanId].Enabled;
                case "CreateDrop":
                    // Always patch CreateDrop so we can award digging XP for panning
                    return true;
                default:
                    break;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnHeldInteractStep")]
        public static void OnHeldInteractStepPrefix(BlockPan __instance, ref float secondsUsed, EntityAgent byEntity)
        {
            Digging digging = XLeveling.Instance(byEntity.Api).GetSkill("digging") as Digging;
            if (digging == null) return;
            PlayerAbility playerAbility = byEntity.GetBehavior<PlayerSkillSet>()?[digging.Id]?[digging.QuickPanId];
            if (playerAbility == null) return;
            secondsUsed *= 1.0f + playerAbility.FValue(0);
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnHeldInteractStop")]
        public static void OnHeldInteractStopPrefix(BlockPan __instance, ref float secondsUsed, EntityAgent byEntity)
        {
            Digging digging = XLeveling.Instance(byEntity.Api).GetSkill("digging") as Digging;
            if (digging == null) return;
            PlayerAbility playerAbility = byEntity.GetBehavior<PlayerSkillSet>()?[digging.Id]?[digging.QuickPanId];
            if (playerAbility == null) return;
            secondsUsed *= 1.0f + playerAbility.FValue(0);
        }

        [HarmonyPrefix]
        [HarmonyPatch("CreateDrop")]
        public static bool CreateDropPrefix(BlockPan __instance, EntityAgent byEntity, string fromBlockCode)
        {
            if (byEntity == null) return true;

            Digging digging = XLeveling.Instance(byEntity.Api).GetSkill("digging") as Digging;
            if (digging == null) return true;

            // Try to get the player's skill object
            PlayerSkill playerSkill = byEntity.GetBehavior<PlayerSkillSet>()?[digging.Id];
            if (playerSkill == null) return true;

            const float xpPerDrop = 0.1f;

            // If the GoldDigger ability is present, handle special drop creation and award XP based on actual generated drops.
            PlayerAbility goldAbility = playerSkill[digging.GoldDiggerId];
            if (goldAbility != null)
            {
                ItemStack[] generated = digging.GeneratePanDrops(byEntity, fromBlockCode, 1.0f + goldAbility.SkillDependentFValue(), 1);

                // award xp based on how many drops were generated
                if (generated != null && generated.Length > 0)
                {
                    playerSkill.AddExperience(xpPerDrop * generated.Length);
                }

                IPlayer player = (byEntity as EntityPlayer)?.Player;
                foreach (ItemStack drop in generated)
                {
                    if (player != null)
                    {
                        if (!player.InventoryManager.TryGiveItemstack(drop, true))
                        {
                            byEntity.Api.World.SpawnItemEntity(drop, byEntity.ServerPos.XYZ);
                        }
                    }
                    else
                    {
                        byEntity.Api.World.SpawnItemEntity(drop, byEntity.ServerPos.XYZ);
                    }
                }

                // prevent the original CreateDrop so we've handled gold-digger behavior
                return false;
            }

            // No GoldDigger ability: estimate how many drops the pan would produce and award XP accordingly.
            // We do not alter the default drop behavior in this case; we only compute potential drops for XP.
            ItemStack[] estimate = digging.GeneratePanDrops(byEntity, fromBlockCode, 1.0f, 8);
            if (estimate != null && estimate.Length > 0)
            {
                playerSkill.AddExperience(xpPerDrop * estimate.Length);
            }

            // Allow original CreateDrop to run for default behavior
            return true;
        }
    }
}