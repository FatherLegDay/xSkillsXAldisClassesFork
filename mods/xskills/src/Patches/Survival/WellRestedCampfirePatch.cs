using System;
using HarmonyLib;
using Vintagestory.API.Common;
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
            if (!(__instance is EntityPlayer player)) return;

            // If campfire checks are disabled in the config, exit early
            var cfg = XLeveling.Instance(__instance.Api)?.Config;
            if (cfg?.disableCampfireChecks == true) return;
            
            float accum = player.Attributes.GetFloat("restAccumLocal", 0f) + dt;

            if (accum < 1f)
            {
                player.Attributes.SetFloat("restAccumLocal", accum);
                return;
            }

            player.Attributes.SetFloat("restAccumLocal", 0f);

            if (!_IsNearFire(__instance, player)) return; 
            
            XEffectsSystem effectSystem = __instance.Api.ModLoader.GetModSystem<XEffectsSystem>();
            if (effectSystem == null) return;

            AffectedEntityBehavior affectedBehavior = player.GetBehavior<AffectedEntityBehavior>();
            if (affectedBehavior == null) return;
            
            Survival survival = XLeveling.Instance(player.Api).GetSkill("survival") as Survival;
            if (survival == null) return;

            PlayerAbility wellRestedAbility = player.GetBehavior<PlayerSkillSet>()?[survival.Id]?[survival.WellRestedId];

            if (wellRestedAbility != null && wellRestedAbility.Tier > 0)
            {
                var existingEffect = affectedBehavior.Effect("rested");
                
                float val1 = wellRestedAbility.Value(1); 
                float val2 = wellRestedAbility.Value(2);

                float maxCapSeconds = Math.Max(val1, val2); 
                float requiredWaitSeconds = Math.Max(1f, Math.Min(val1, val2)); 
                float expMultiplier = wellRestedAbility.FValue(0); 

                float buffPerSecond = maxCapSeconds / requiredWaitSeconds;
                float addDurationThisTick = (buffPerSecond * accum) + accum;

                if (existingEffect != null)
                {
                    float newDuration = existingEffect.Runtime;
                    newDuration = newDuration - addDurationThisTick;
                    newDuration = Math.Clamp(newDuration, 0, maxCapSeconds);
                    existingEffect.Runtime = newDuration;
                    existingEffect.Update(expMultiplier);
                }
                else
                {
                    if (__instance.World.Side == EnumAppSide.Server)
                    {
                        Effect newEffect = effectSystem.CreateEffect("rested");
                        if (newEffect != null)
                        {
                            newEffect.Duration = maxCapSeconds;
                            newEffect.Runtime = maxCapSeconds - addDurationThisTick;
                            newEffect.Update(expMultiplier);
                            affectedBehavior.AddEffect(newEffect);
                        }
                    }
                }
            }
        }

        // IsNearFire got seperated out as a performance optimization.
        // as soon as we find a campfire, we don't need to search for any others. we can just exit the loop immediately.
        private static bool _IsNearFire(EntityAgent __instance, EntityPlayer player)
        {
            // We use 'Pos' instead of 'ServerPos' because this now evaluates on the Client side too!
            BlockPos pos = player.Pos.AsBlockPos;
            BlockPos tmpPos = new BlockPos(pos.dimension);
            
            // Check a 7x3x7 area around the player for lit firepits
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        tmpPos.Set(pos.X + dx, pos.Y + dy, pos.Z + dz);
                        Block block = __instance.World.BlockAccessor.GetBlock(tmpPos);

                        if (block?.Code?.Path.Contains("firepit-lit") == true)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}