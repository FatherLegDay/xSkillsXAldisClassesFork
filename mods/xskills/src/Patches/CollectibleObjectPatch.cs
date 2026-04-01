using HarmonyLib;
using System;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using XLib.XLeveling;

namespace XSkills
{
    [HarmonyPatch(typeof(CollectibleObject))]
    public class CollectibleObjectPatch
    {
        //public static bool Prepare(MethodBase original)
        //{
        //    XSkills xSkills = XSkills.Instance;
        //    if (xSkills == null) return false;

        //    if (original.Name == "TryMergeStacks")
        //    {
        //        return !xskills.XLeveling.Config.mergeQualities;
        //    }
        //    else return true;
        //}

        [HarmonyPrefix]
        [HarmonyPatch("SetTemperature")]
        public static void SetTemperaturePrefix(IWorldAccessor world, ItemStack itemstack, float temperature, bool delayCooldown = true)
        {
            ITreeAttribute attr = (ITreeAttribute)itemstack?.Attributes["temperature"];
            if (attr == null) return;
            float temp = (float)attr.GetDecimal("temperature");
            float diff = Math.Min(temperature, 1050.0f) - Math.Min(temp, 1050.0f);
            float quality = itemstack.Attributes.GetFloat("quality", 0.0f);

            if (quality > 0.0f && (diff < -4.5f || diff > 0.0f))
            {
                //equals 2.0 quality at 1000 °C
                itemstack.Attributes.SetFloat("quality", Math.Max(quality - diff * 0.002f, 0.01f));
            }
        }

        [HarmonyPatch("OnBlockBrokenWith")]
        public static void Prefix(ref Block __state, IWorldAccessor world, BlockSelection blockSel)
        {
            __state = world.BlockAccessor.GetBlock(blockSel.Position);
        }

        [HarmonyPatch("OnBlockBrokenWith")]
        public static void Postfix(CollectibleObject __instance, Block __state, IWorldAccessor world, Entity byEntity, ItemSlot itemslot)
        {
            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = (byEntity as EntityPlayer)?.Player;
            ItemStack itemstack = itemslot.Itemstack;
            if (itemstack == null || byPlayer == null) return;
            if (__state == null) return;
            DropBonusBehavior beh = null;

            foreach (BlockBehavior beh2 in __state.BlockBehaviors)
            {
                beh = beh2 as DropBonusBehavior;
                if (beh != null) break;
            }

            Item tool = itemstack.Item;
            if (tool == null || beh == null) return;
            if ((beh.Tool != tool.Tool) && !tool.Code.Path.Contains("paxel")) return;

            //durability
            if (__instance.DamagedBy != null && __instance.DamagedBy.Contains(EnumItemDamageSource.BlockBreaking))
            {
                //for multiplayer server the clients sometimes don't set the forestry skill properly
                //and i don't know why. This should fix it. 
                if(beh.Skill == null)
                {
                    if (beh is XSkillsCharcoalBehavior charcoalBehavior)
                    {
                        charcoalBehavior.Forestry = XLeveling.Instance(world.Api)?.GetSkill("forestry") as Forestry;
                    }
                    if (beh.Skill == null) return;
                }

                PlayerAbility playerAbility = byEntity.GetBehavior<PlayerSkillSet>()?[beh.Skill.Id]?[beh.Skill.DurabilityId];

                if (playerAbility != null && (playerAbility.SkillDependentFValue() >= world.Rand.NextDouble()))
                {
                    int leftDurability = itemstack.Attributes.GetInt("durability", __instance.GetMaxDurability(itemstack));
                    leftDurability += 1;
                    itemstack.Attributes.SetInt("durability", leftDurability);
                    itemslot.MarkDirty();
                }
            }
        }

        [HarmonyPatch("GetHeldItemInfo")]
        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
        {
            float quality = inSlot?.Itemstack?.Attributes.TryGetFloat("quality") ?? 0.0f;

            // Always add the quality display line only when quality > 0 (the helper already guards this)
            QualityUtil.AddQualityString(quality, dsc);

            // --- Remove any standalone "Damage: +X%" fallback lines that may have been added elsewhere ---
            if (quality > 0.0f)
            {
                try
                {
                    string current = dsc.ToString();
                    // remove lines like: Damage: +25.3% or formatted <font ...>Damage: +25.3%</font>
                    current = Regex.Replace(current, @"(?m)^\s*(?:<font[^>]*>)?\s*Damage:\s*\+[0-9]+(?:[.,][0-9]+)?%\s*(?:<\/font>)?\s*$", "", RegexOptions.IgnoreCase);
                    dsc.Clear();
                    dsc.Append(current);
                }
                catch { /* swallow errors - tooltip mustn't break */ }
            }

            // Only modify existing tooltip lines when the item actually has a positive quality.
            if (quality > 0.0f)
            {
                // Color CombatOverhaul damage lines (e.g. "Two-handed: 8.8 (6 tier) Slashing", "Two-handed bash: 6.3 (6 tier) Slashing")
                // We don't change numbers here, instead we append the bonus percent (increase vs. base) after the damage number and color it.
                try
                {
                    string full = dsc.ToString();
                    string color = QualityUtil.QualityColor(quality);
                    float bonusPercent = (QualityUtil.GetDamageMultiplier(quality) - 1.0f) * 100.0f;

                    // Match lines that look like CombatOverhaul damage entries containing "(N tier)"
                    // Group 1: prefix up to the numeric value and colon/space
                    // Group 2: numeric value
                    // Group 3: the rest including "(N tier) ..."
                    string pattern = @"(?m)^(.*?:\s*)(-?\d+(?:[.,]\d+)?)(\s*\(\d+\s+tier\).*)$";
                    string replaced = Regex.Replace(full, pattern, m =>
                    {
                        // keep the displayed number, append colored "(+X.X%)"
                        return m.Groups[1].Value
                            + m.Groups[2].Value
                            + " <font color=\"" + color + "\">(+" + bonusPercent.ToString("N1", CultureInfo.InvariantCulture) + "%)</font>"
                            + m.Groups[3].Value;
                    }, RegexOptions.IgnoreCase);

                    if (!object.ReferenceEquals(replaced, full) && replaced != full)
                    {
                        dsc.Clear();
                        dsc.Append(replaced);
                    }
                }
                catch { /* swallow errors */ }

                // Mining speed: modify only if an existing "Mining Speed:" line is present (no fallback append)
                try
                {
                    string full = dsc.ToString();
                    int idx = full.IndexOf("Mining Speed:", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        int lineStart = full.LastIndexOf('\n', idx);
                        lineStart = (lineStart >= 0) ? (lineStart + 1) : 0;
                        int lineEnd = full.IndexOf('\n', idx);
                        if (lineEnd < 0) lineEnd = full.Length;

                        string line = full.Substring(lineStart, lineEnd - lineStart);

                        float multiplier = QualityUtil.GetMiningSpeedMultiplier(quality);
                        string replaced = Regex.Replace(line, @"(\d+(?:[.,]\d+)?)(?=x)", m =>
                        {
                            if (!float.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                                return m.Value;
                            float newVal = val * multiplier;
                            return newVal.ToString("#.#", CultureInfo.InvariantCulture);
                        });

                        string color = QualityUtil.QualityColor(quality);
                        int colonIdx = replaced.IndexOf(':');
                        string finalLine;
                        if (colonIdx >= 0)
                        {
                            string prefix = replaced.Substring(0, colonIdx + 1);
                            string rest = replaced.Substring(colonIdx + 1).TrimStart();
                            finalLine = prefix + " <font color=\"" + color + "\">" + rest + "</font>";
                        }
                        else
                        {
                            finalLine = "<font color=\"" + color + "\">" + replaced + "</font>";
                        }

                        dsc.Remove(lineStart, lineEnd - lineStart);
                        dsc.Insert(lineStart, finalLine);
                    }
                }
                catch
                {
                    // swallow errors and DO NOT append fallback mining-speed line
                }

                // Modify "Durability:" line to color values and append increased percent in quality color.
                try
                {
                    string full = dsc.ToString();
                    int dIdx = full.IndexOf("Durability:", StringComparison.OrdinalIgnoreCase);
                    if (dIdx >= 0)
                    {
                        int lineStart = full.LastIndexOf('\n', dIdx);
                        lineStart = (lineStart >= 0) ? (lineStart + 1) : 0;
                        int lineEnd = full.IndexOf('\n', dIdx);
                        if (lineEnd < 0) lineEnd = full.Length;

                        string line = full.Substring(lineStart, lineEnd - lineStart);

                        Match m = Regex.Match(line, @"(\d+)\s*/\s*(\d+)");
                        string color = QualityUtil.QualityColor(quality);
                        float increasePercent = quality * 5.0f;

                        string finalLine;
                        if (m.Success)
                        {
                            int colonIdx = line.IndexOf(':');
                            string prefix = colonIdx >= 0 ? line.Substring(0, colonIdx + 1) : "Durability:";
                            string values = m.Value;
                            string restAfterValues = line.Substring(line.IndexOf(values) + values.Length).TrimStart();

                            finalLine = prefix + " <font color=\"" + color + "\">" + values;
                            if (!string.IsNullOrEmpty(restAfterValues))
                            {
                                finalLine += " " + restAfterValues;
                            }
                            finalLine += " Increased by " + increasePercent.ToString("F1", CultureInfo.InvariantCulture) + "%</font>";
                        }
                        else
                        {
                            int colonIdx = line.IndexOf(':');
                            string prefix = colonIdx >= 0 ? line.Substring(0, colonIdx + 1) : "Durability:";
                            string rest = colonIdx >= 0 ? line.Substring(colonIdx + 1).TrimStart() : "";
                            finalLine = prefix + " <font color=\"" + color + "\">" + rest + " Increased by " + increasePercent.ToString("F1", CultureInfo.InvariantCulture) + "%</font>";
                        }

                        dsc.Remove(lineStart, lineEnd - lineStart);
                        dsc.Insert(lineStart, finalLine);
                    }
                    else
                    {
                        if (inSlot?.Itemstack != null)
                        {
                            int currentDur = inSlot.Itemstack.Attributes.GetInt("durability", inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack));
                            int maxDur = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack);
                            string color = QualityUtil.QualityColor(quality);
                            float increasePercent = quality * 5.0f;
                            dsc.AppendLine($"Durability: <font color=\"{color}\">{currentDur} / {maxDur} Increased by {increasePercent.ToString("F1", CultureInfo.InvariantCulture)}%</font>");
                        }
                    }
                }
                catch
                {
                    // ignore durability tooltip errors
                }

                // Replace numeric part of existing "Attack power" line with colored adjusted value when present and append bonus percent.
                try
                {
                    string full = dsc.ToString();
                    int aIdx = full.IndexOf("Attack power:", StringComparison.OrdinalIgnoreCase);
                    if (aIdx >= 0)
                    {
                        int lineStart = full.LastIndexOf('\n', aIdx);
                        lineStart = (lineStart >= 0) ? (lineStart + 1) : 0;
                        int lineEnd = full.IndexOf('\n', aIdx);
                        if (lineEnd < 0) lineEnd = full.Length;

                        string line = full.Substring(lineStart, lineEnd - lineStart);

                        Match m = Regex.Match(line, @"(-?\d+(?:[.,]\d+)?)(?=\s*(?:hp\b)?)", RegexOptions.IgnoreCase);
                        if (m.Success && float.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var baseVal))
                        {
                            float mul = QualityUtil.GetDamageMultiplier(quality);
                            float newVal = baseVal * mul;
                            string color = QualityUtil.QualityColor(quality);
                            float bonusPercent = (mul - 1.0f) * 100.0f;

                            // Replace only the numeric portion with a colored numeric value and append the bonus percent
                            string replacedLine = line.Substring(0, m.Index)
                                + "<font color=\"" + color + "\">" + newVal.ToString("N1", CultureInfo.InvariantCulture) + " (+" + bonusPercent.ToString("N1", CultureInfo.InvariantCulture) + "%)</font>"
                                + line.Substring(m.Index + m.Length);

                            dsc.Remove(lineStart, lineEnd - lineStart);
                            dsc.Insert(lineStart, replacedLine);
                        }
                    }
                }
                catch
                {
                    // swallow errors; do not append fallback damage
                }
            } // end quality > 0

            // Show "created by" tooltip (supports multiple creators in order if stored as a delimited string)
            try
            {
                var attrs = inSlot?.Itemstack?.Attributes;
                if (attrs != null && attrs.HasAttribute("createdBy"))
                {
                    string createdByStr = attrs.GetString("createdBy") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(createdByStr))
                    {
                        // support several common delimiters so multiple creators are shown in the order they were stored
                        char[] separators = new char[] { ',', '|', ';' };
                        string[] parts = createdByStr.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(p => p.Trim())
                                                     .ToArray();

                        if (parts.Length == 1)
                        {
                            string uid = parts[0];
                            string playerName = world?.PlayerByUid(uid)?.PlayerName ?? uid;
                            dsc.AppendLine();
                            dsc.Append("Created by: ");
                            dsc.Append(playerName);
                        }
                        else if (parts.Length > 1)
                        {
                            dsc.AppendLine();
                            dsc.Append("Created by: ");
                            for (int i = 0; i < parts.Length; i++)
                            {
                                string uid = parts[i];
                                string playerName = world?.PlayerByUid(uid)?.PlayerName ?? uid;
                                if (i > 0) dsc.Append(", ");
                                dsc.Append(playerName);
                            }
                        }
                    }
                }
            }
            catch
            {
                // swallow any unexpected errors in tooltip rendering to avoid interfering with game UI
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetMaxDurability")]
        public static void Postfix0(ref int __result, ItemStack itemstack)
        {
            float quality = itemstack?.Attributes.TryGetFloat("quality") ?? 0.0f;
            if (quality > 0.0f && __result > 1) __result = (int)(__result * (1.0f + quality * 0.05f));
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetAttackPower")]
        public static void Postfix1(ref float __result, IItemStack withItemStack)
        {
            float quality = withItemStack?.Attributes.TryGetFloat("quality") ?? 0.0f;
            if (quality > 0.0f && __result > 0.5f)
            {
                __result = (float)(__result * QualityUtil.GetDamageMultiplier(quality));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetMiningSpeed")]
        public static void Postfix2(ref float __result, IItemStack itemstack)
        {
            float quality = itemstack?.Attributes.TryGetFloat("quality") ?? 0.0f;
            if (quality > 0.0f) __result = (float)(__result * QualityUtil.GetMiningSpeedMultiplier(quality));
        }

        [HarmonyPatch("OnCreatedByCrafting")]
        public static void Postfix(ItemSlot[] allInputslots, ItemSlot outputSlot)
        {
            if (outputSlot.Itemstack == null) return;
            int maxDurability = outputSlot.Itemstack.Collectible.GetMaxDurability(outputSlot.Itemstack);

            if (maxDurability <= 1) return;
            float quality = 0.0f;
            int count = 0;
            bool useQuality = false;
            foreach(ItemSlot slot in allInputslots)
            {
                if (slot.Itemstack == null) continue;
                float? inputQuality = slot.Itemstack.Attributes.TryGetFloat("quality");

                if (outputSlot.Itemstack.Collectible == slot.Itemstack.Collectible)
                {
                    quality += (inputQuality ?? 0.0f) * 8.0f;
                    count += 8;
                }
                else if (slot.Itemstack.Collectible.Attributes?.KeyExists("qualityType") ?? false)
                {
                    useQuality = true;
                    quality += inputQuality ?? 0.0f;
                    count++;
                }
                else if (inputQuality != null)
                {
                    useQuality = true;
                    quality += 2 * (inputQuality ?? 0.0f);
                    count += 2;
                }
            }
            if (count > 0 && useQuality)
            {
                quality /= count;
                if (quality > 0.05f) outputSlot.Itemstack.Attributes.SetFloat("quality", quality);
            }

            // Propagate "createdBy" from any input to the crafted output so a smithed origin survives crafting.
            // Only apply for items that support durability (same domain where quality was applied)
            foreach (ItemSlot slot in allInputslots)
            {
                if (slot?.Itemstack == null) continue;
                string createdBy = slot.Itemstack.Attributes.GetString("createdBy");
                if (!string.IsNullOrEmpty(createdBy))
                {
                    outputSlot.Itemstack.Attributes.SetString("createdBy", createdBy);
                    break;
                }
            }
        }

        [HarmonyPatch("TryMergeStacks")]
        public static bool Prefix(out ItemStack __state, ItemStackMergeOperation op)
        {
            __state = op.SourceSlot.Itemstack;
            if (op.CurrentPriority != EnumMergePriority.AutoMerge) return true;
            if (!(op.SourceSlot.Itemstack.Collectible.Attributes?.KeyExists("qualityType") ?? false)) return true;
            if (op.SourceSlot.Itemstack.Attributes.GetDecimal("quality") == 
                op.SinkSlot.Itemstack.Attributes.GetDecimal("quality")) return true;
            return XSkills.Instance.XLeveling.Config.mergeQualities;
        }

        [HarmonyPatch("TryMergeStacks")]
        public static void Postfix(ItemStack __state, ItemStackMergeOperation op)
        {
            if (op.MovedQuantity <= 0) return;
            if (__state?.Attributes == null || op.SinkSlot.Itemstack?.Attributes == null) return;
            float quality = (
                __state.Attributes.GetFloat("quality") * (op.MovedQuantity) + 
                op.SinkSlot.Itemstack.Attributes.GetFloat("quality") * (op.SinkSlot.Itemstack.StackSize - op.MovedQuantity)) / 
                op.SinkSlot.Itemstack.StackSize;
            if (quality > 0.0f) op.SinkSlot.Itemstack.Attributes.SetFloat("quality", quality);
        }

        public class TryEatStopState
        {
            public float quality;
            public float size;
            public float temperature;
            public EnumFoodCategory foodCategory;

            public TryEatStopState()
            {
                quality = 0.0f;
                size = 0.0f;
                temperature = 0.0f;
                foodCategory = EnumFoodCategory.NoNutrition;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("tryEatStop")]
        public static void tryEatStopPrefix(CollectibleObject __instance, out TryEatStopState __state, float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            __state = new TryEatStopState();
            if (secondsUsed < 0.95f || byEntity?.World.Api.Side == EnumAppSide.Client) return;
            if (byEntity == null || slot?.Itemstack == null) return;
            FoodNutritionProperties nutriProps = __instance.GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity);
            if (nutriProps == null) return;

            __state.quality = slot.Itemstack.Attributes?.GetFloat("quality") ?? 0;
            __state.size = slot.Itemstack.StackSize;
            __state.temperature = slot.Itemstack.Collectible.GetTemperature(byEntity.World, slot.Itemstack);
            __state.foodCategory = nutriProps.FoodCategory;
        }

        [HarmonyPostfix]
        [HarmonyPatch("tryEatStop")]
        public static void tryEatStopPostfix(TryEatStopState __state, float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            if (byEntity?.World.Api.Side == EnumAppSide.Server && __state.foodCategory != EnumFoodCategory.NoNutrition && secondsUsed >= 0.95f)
            {
                Cooking.ApplyQuality(
                    __state.quality, __state.size - (slot.Itemstack?.StackSize ?? 0), 
                    __state.temperature, __state.foodCategory, 
                    EnumFoodCategory.Unknown, byEntity);
            }
        }

        [HarmonyPatch("DoSmelt")]
        public static void Prefix(out DoSmeltState __state, ItemSlot outputSlot)
        {
            __state = new DoSmeltState();
            __state.stackSize = outputSlot.Itemstack?.StackSize ?? 0;
            __state.quality = outputSlot.Itemstack?.Attributes.GetFloat("quality") ?? 0.0f;
        }

        [HarmonyPatch("DoSmelt")]
        public static void Postfix(DoSmeltState __state, IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot outputSlot)
        {
            InventoryBase inv = cookingSlotsProvider as InventoryBase;
            if (inv == null) return;
            BlockEntity blockEntity = world?.BlockAccessor.GetBlockEntity(inv.Pos);
            BlockEntityBehaviorOwnable ownable = blockEntity?.GetBehavior<BlockEntityBehaviorOwnable>();

            int cooked = (outputSlot.Itemstack?.StackSize ?? 0) - __state.stackSize;
            if (ownable?.Owner == null || cooked <= 0) return;
            DoSmeltCooking(ownable.Owner, outputSlot, cooked, __state.quality);
        }

        internal static bool DoSmeltCooking(IPlayer byPlayer, ItemSlot outputSlot, int cooked, float quality)
        {
            FoodNutritionProperties nutritionProps = outputSlot.Itemstack?.Collectible.NutritionProps;
            if (nutritionProps == null) return false;

            Cooking cooking = byPlayer.Entity?.Api.ModLoader.GetModSystem<XLeveling>()?.GetSkill("cooking") as Cooking;
            if (cooking == null) return true;
            cooking.ApplyAbilities(outputSlot, byPlayer, quality, cooked);
            return true;
        }
    }//!class CollectiblePatch
}//!namespace XSkills
