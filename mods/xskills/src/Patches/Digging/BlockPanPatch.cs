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
                    // Always patch CreateDrop so we can award digging XP for panning even if GoldDigger is not enabled
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

            // If the GoldDigger ability is present, handle special drop creation, award XP based on actual generated drops,
            // give/spawn them and prevent the original method.
            PlayerAbility goldAbility = playerSkill[digging.GoldDiggerId];
            if (goldAbility != null)
            {
                ItemStack[] generated = digging.GeneratePanDrops(byEntity, fromBlockCode, 1.0f + goldAbility.SkillDependentFValue(), 1);

                // Sum XP per produced stack using Digging.CalculatePanningXp
                float totalXp = 0.0f;
                if (generated != null && generated.Length > 0)
                {
                    foreach (ItemStack stack in generated)
                    {
                        totalXp += digging.CalculatePanningXp(stack);
                    }
                    playerSkill.AddExperience(totalXp);
                }

                IPlayer player = (byEntity as EntityPlayer)?.Player;
                foreach (ItemStack drop in generated)
                {
                    if (player != null)
                    {
                        if (!player.InventoryManager.TryGiveItemstack(drop, true))
                        {
                            byEntity.Api.World.SpawnItemEntity(drop, byEntity.Pos.XYZ);
                        }
                    }
                    else
                    {
                        byEntity.Api.World.SpawnItemEntity(drop, byEntity.Pos.XYZ);
                    }
                }

                return false;
            }

            // No GoldDigger ability: estimate potential drops and award XP using the same mapping.
            ItemStack[] estimate = digging.GeneratePanDrops(byEntity, fromBlockCode, 1.0f, 8);
            if (estimate != null && estimate.Length > 0)
            {
                float totalXp = 0.0f;
                foreach (ItemStack stack in estimate)
                {
                    totalXp += digging.CalculatePanningXp(stack);
                }
                playerSkill.AddExperience(totalXp);
            }

            // Allow original CreateDrop to run for default behavior
            return true;
        }
    }
}