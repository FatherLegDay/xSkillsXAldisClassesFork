using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using XLib.XEffects;
using XLib.XLeveling;

namespace XSkills
{
    [HarmonyPatch(typeof(EntityAgent), "OnGameTick")]
    public class WellRestedCampfirePatch
    {
        static void Postfix(EntityAgent __instance, float dt)
        {
            if (__instance.World.Side != EnumAppSide.Server) return;
            if (!(__instance is EntityPlayer player)) return;

            float accum = player.WatchedAttributes.GetFloat("restAccum", 0f) + dt;

            if (accum < 0.5f)
            {
                player.WatchedAttributes.SetFloat("restAccum", accum);
                return;
            }

            player.WatchedAttributes.SetFloat("restAccum", 0f);

            // Get systems
            XEffectsSystem effectSystem = __instance.Api.ModLoader.GetModSystem<XEffectsSystem>();
            if (effectSystem == null) return;

            AffectedEntityBehavior affectedBehavior = player.GetBehavior<AffectedEntityBehavior>();
            if (affectedBehavior == null) return;

            // Fire Detection
            BlockPos pos = player.ServerPos.AsBlockPos;
            BlockPos tmpPos = new BlockPos(pos.dimension);
            bool nearFire = false;

            // Check a 7x3x7 area around the player for lit firepits
            for (int dx = -3; dx <= 3 && !nearFire; dx++)
            {
                for (int dy = -1; dy <= 1 && !nearFire; dy++)
                {
                    for (int dz = -3; dz <= 3 && !nearFire; dz++)
                    {
                        tmpPos.Set(pos.X + dx, pos.Y + dy, pos.Z + dz);
                        Block block = __instance.World.BlockAccessor.GetBlock(tmpPos);

                        if (block?.Code?.Path.Contains("firepit-lit") == true)
                        {
                            nearFire = true;
                        }
                    }
                }
            }

            // Manage rest time
            float restTime = player.WatchedAttributes.GetFloat("restTime", 0f);

            if (nearFire)
            {
                restTime += accum;
            }
            else
            {
                restTime = 0f;
            }

            player.WatchedAttributes.SetFloat("restTime", restTime);

            // If the player has been resting for less than set ammount of seconds, don't apply the effect divided by 10 to make it = set ammount of seconds as the accum is 0.5 seconds
            // (This check is now driven by the configured ability value instead of hard-coded constant)

            // Apply effect
            XSkillsPlayerBehavior pbh = player.GetBehavior("XSkillsPlayer") as XSkillsPlayerBehavior;
            if (pbh == null) return;

            Survival survival = XLeveling.Instance(__instance.Api).GetSkill("survival") as Survival;
            if (survival == null) return;
            PlayerAbility playerAbility = player.GetBehavior<PlayerSkillSet>()?[survival.Id]?[survival.WellRestedId];
            if (playerAbility == null) return;
            if (playerAbility.Tier < 1) return;

            // Pull intensity (index 0), duration (index 1) and required rest time (index 2) from the wellrested ability
            float intensity = (float)playerAbility.FValue(0);
            float duration = (float)playerAbility.Value(1);
            float requiredRest = (float)playerAbility.Value(2);

            // If the player has been resting for less than the configured required rest time, don't apply the effect
            if (restTime < requiredRest)
            {
                return;
            }

            var active = affectedBehavior.Effect("rested");

            if (active == null)
            {
                active = effectSystem.CreateEffect("rested");
                if (active == null)
                    return;

                // set intensity and duration from ability values
                active.Update(intensity);
                active.Duration = duration;
                affectedBehavior.AddEffect(active);
            }
            else
            {
                // update existing effect to match current ability values
                active.Update(intensity);
                active.Duration = duration;
            }
        }
    }
}
