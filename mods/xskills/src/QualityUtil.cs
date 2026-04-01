using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace XSkills
{
    /// <summary>
    /// Small helper class to save the quality state.
    /// </summary>
    public class QualityState
    {
        /// <summary>
        /// The quality
        /// </summary>
        public float quality;

        /// <summary>
        /// The old quantity
        /// </summary>
        public float oldQuantity;

        /// <summary>
        /// The old quality
        /// </summary>
        public float oldQuality;
    }//!class QualityState

    /// <summary>
    /// Contains some quality related methods.
    /// </summary>
    public class QualityUtil
    {
        /// <summary>
        /// Gets the quality.
        /// </summary>
        /// <param name="slot">The slot.</param>
        /// <returns></returns>
        public static float GetQuality(ItemSlot slot)
        {
            if (slot == null) return 0.0f;
            return GetQuality(slot.Itemstack);
        }

        /// <summary>
        /// Gets the quality.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <returns></returns>
        public static float GetQuality(ItemStack stack)
        {
            if (stack == null) return 0.0f;
            return stack.Attributes.GetFloat("quality", 0.0f);
        }

        /// <summary>
        /// Gets the quality for a placed block.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="pos">The position.</param>
        /// <returns></returns>
        public static float GetQuality(IWorldAccessor world, BlockPos pos)
        {
            BlockEntityCookedContainer bec = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCookedContainer;
            if (bec == null) return 0.0f;
            return GetQuality(bec.Inventory[0]?.Itemstack);
        }

        /// <summary>
        /// Adds the quality string.
        /// </summary>
        /// <param name="inSlot">The slot.</param>
        /// <param name="dsc">The string builder.</param>
        public static void AddQualityString(ItemSlot slot, StringBuilder dsc)
        {
            AddQualityString(GetQuality(slot), dsc);
        }

        /// <summary>
        /// Adds the quality string to a string builder.
        /// </summary>
        /// <param name="quality">The quality.</param>
        /// <param name="dsc">The string builder.</param>
        public static void AddQualityString(float quality, StringBuilder dsc)
        {
            if (quality > 0.0f)
            {
                string str = QualityString(quality);
                dsc.AppendLine(str);
            }
        }

        /// <summary>
        /// Returns the mining-speed multiplier for a given quality.
        /// Piecewise: 0-5, 5-10, 10-15 use different slopes/curves so each range
        /// can be tuned independently.
        /// </summary>
        /// <param name="quality">The quality value.</param>
        /// <returns>Multiplier to apply to mining speed (e.g., 1.0 = no change).</returns>
        public static float GetMiningSpeedMultiplier(float quality)
        {
            if (quality <= 0.0f) return 1.0f;

            // Range 0 - 5: gentle scaling (e.g. 1% per quality)
            if (quality <= 5.0f)
            {
                return 1.0f + quality * 0.01f;
            }

            // Range 5 - 10: stronger scaling (base from first range + 3% per point above 5)
            if (quality <= 10.0f)
            {
                float baseAt5 = 1.0f + 5.0f * 0.01f; // = 1.05
                return baseAt5 + (quality - 5.0f) * 0.03f;
            }

            // Range 10 - 15: strongest scaling (base from second range + 5% per point above 10)
            // Note: clamp at 15 if you want a hard cap - currently this continues past 15 with the same formula.
            float baseAt10 = 1.0f + 5.0f * 0.01f + 5.0f * 0.03f; // = 1.05 + 0.15 = 1.20
            return baseAt10 + (quality - 10.0f) * 0.05f;
        }

        /// <param name="quality">The quality value.</param>
        /// <param name="dsc">The string builder.</param>
        // returns damage multiplier based on piecewise quality ranges so q=15 => +30%
        public static float GetDamageMultiplier(float quality)
        {
            if (quality <= 0.0f) return 1.0f;

            // 0-5: +1% per point
            if (quality <= 5.0f)
            {
                return 1.0f + quality * 0.01f;
            }

            // 5-10: +2% per point above 5
            if (quality <= 10.0f)
            {
                float baseAt5 = 1.0f + 5.0f * 0.01f; // 1.05
                return baseAt5 + (quality - 5.0f) * 0.02f;
            }

            // 10-15: +3% per point above 10 (so total at 15 = 1.0 + 5*(0.01+0.02+0.03)=1.30)
            float baseAt10 = 1.0f + 5.0f * 0.01f + 5.0f * 0.02f; // 1.20
            return baseAt10 + (quality - 10.0f) * 0.03f;
        }

        public static void AddDamageString(float quality, StringBuilder dsc)
        {
            if (quality > 0.0f)
            {
                float mul = GetDamageMultiplier(quality);
                float bonusPercent = (mul - 1.0f) * 100.0f;
                string color = QualityColor(quality);
                dsc.AppendLine(string.Format("<font color=\"{0}\">Damage: +{1:N1}%</font>", color, bonusPercent));
            }
        }
        public static void AddMiningSpeedString(float quality, StringBuilder dsc)
        {
            if (quality > 0.0f)
            {
                float mul = GetMiningSpeedMultiplier(quality);
                float bonusPercent = (mul - 1.0f) * 100.0f;
                string color = QualityColor(quality);
                // Use one decimal to show partial percent values for clarity
                dsc.AppendLine(string.Format("<font color=\"{0}\">Mining speed: +{1:N1}%</font>", color, bonusPercent));
            }
        }

        /// <summary>
        /// Picks the quality from a BlockEntityCookedContainer at a position
        /// and transfers it into a stack.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="world">The world.</param>
        /// <param name="pos">The position.</param>
        public static void PickQuality(ItemStack stack, IWorldAccessor world, BlockPos pos)
        {
            float quality = GetQuality(world, pos);
            if (quality <= 0.0f) return;
            stack.Attributes.SetFloat("quality", quality);
        }

        /// <summary>
        /// Gets the type of the quality for a collectible.
        /// Types can be: "tool", "armor", "weapon"
        /// </summary>
        /// <param name="collectible">The collectible.</param>
        /// <returns></returns>
        public static string GetQualityType(CollectibleObject collectible)
        {
            if (collectible == null) return null;
            int type = collectible.Attributes?["qualityType"]?.AsInt(-1) ?? -1;
            if (type == -1)
            {
                switch (collectible.Tool)
                {
                    case EnumTool.Chisel:
                    case EnumTool.Shears:
                    case EnumTool.Wrench:
                        type = 0;
                        break;
                }
            }
            if (type < 0)
            {
                if (collectible is ItemWearableAttachment) type = 1;
                else return null;
            }
            string str = null;
            switch (type)
            {
                case 0:
                    str = "tool";
                    break;
                case 1:
                    str = "armor";
                    break;
                case 2:
                    str = "weapon";
                    break;
            }
            return str;
        }

        /// <summary>
        /// Converts the quality to a string representing its value.
        /// </summary>
        /// <param name="quality">The quality.</param>
        /// <param name="formatted">if set to <c>true</c> the string will contain some code to format.</param>
        /// <returns></returns>
        public static string QualityString(float quality, bool formatted = true)
        {
            if (quality > 0.0f)
            {
                if (formatted)
                {
                    string color = QualityColor(quality);
                    if (quality < 1.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-bad") + "({1:N2})</font>", color, quality);
                    else if (quality < 2.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-common") + "({1:N2})</font>", color, quality);
                    else if (quality < 4.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-uncommon") + "({1:N2})</font>", color, quality);
                    else if (quality < 6.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-rare") + "({1:N2})</font>", color, quality);
                    else if (quality < 8.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-epic") + "({1:N2})</font>", color, quality);
                    else if (quality < 10.0f) return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-legendary") + "({1:N2})</font>", color, quality);
                    else return string.Format("<font color=\"{0}\">" + Lang.Get("xskills:quality-mythic") + "({1:N2})</font>", color, quality);
                }
                else
                {
                    if (quality < 1.0f) return string.Format(Lang.Get("xskills:quality-bad") + "({0:N2})", quality);
                    else if (quality < 2.0f) return string.Format(Lang.Get("xskills:quality-common") + "({0:N2})", quality);
                    else if (quality < 4.0f) return string.Format(Lang.Get("xskills:quality-uncommon") + "({0:N2})", quality);
                    else if (quality < 6.0f) return string.Format(Lang.Get("xskills:quality-rare") + "({0:N2})", quality);
                    else if (quality < 8.0f) return string.Format(Lang.Get("xskills:quality-epic") + "({0:N2})", quality);
                    else if (quality < 10.0f) return string.Format(Lang.Get("xskills:quality-legendary") + "({0:N2})", quality);
                    else return string.Format(Lang.Get("xskills:quality-mythic") + "({0:N2})", quality);
                }
            }
            return "";
        }

        public static string QualityColor(float quality)
        {
            if (quality < 1.0f) return "#808080";   // gray
            if (quality < 2.0f) return "#FFFFFF";   // white
            if (quality < 4.0f) return "#00FF00";   // green
            if (quality < 6.0f) return "#001EFF";   // blue
            if (quality < 8.0f) return "#DD00FF";   // epic (magenta)
            if (quality < 10.0f) return "#FFAA00";  // legendary (orange)
            return "#00E6FF";                       // mythic (cyan)
        }
    }//!class QualityUtil
}//!namespace XSkills
