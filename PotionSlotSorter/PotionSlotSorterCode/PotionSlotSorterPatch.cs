using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace PotionSlotSorter.PotionSlotSorterCode;

[HarmonyPatch]
public static class PotionSlotSorterPatch
{
    private static readonly FieldInfo? PotionSlotsField =
        typeof(Player).GetField("_potionSlots", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? HoldersField =
        typeof(NPotionContainer).GetField("_holders", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PlayerField =
        typeof(NPotionContainer).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? HolderPotionProperty =
        typeof(NPotionHolder).GetProperty(nameof(NPotionHolder.Potion), BindingFlags.Instance | BindingFlags.Public);

    private static readonly FieldInfo? EmptyIconField =
        typeof(NPotionHolder).GetField("_emptyIcon", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? EmptyPotionTweenField =
        typeof(NPotionHolder).GetField("_emptyPotionTween", BindingFlags.Instance | BindingFlags.NonPublic);

    // Patch Player (not UI handlers) so both use and discard are covered after belt animations are kicked off.
    [HarmonyPatch(typeof(Player), nameof(Player.RemoveUsedPotionInternal))]
    public static class PlayerRemoveUsedPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => TryCompactAndResyncFromRun();
    }

    [HarmonyPatch(typeof(Player), nameof(Player.DiscardPotionInternal))]
    public static class PlayerDiscardPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => TryCompactAndResyncFromRun();
    }

    private static void TryCompactAndResyncFromRun()
    {
        var container = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer;
        if (container != null)
        {
            TryCompactAndResync(container);
        }
    }

    private static void TryCompactAndResync(NPotionContainer container)
    {
        var player = GetField<Player>(container, PlayerField);
        if (player == null)
        {
            return;
        }

        CompactPotionSlots(player);

        var holders = GetField<List<NPotionHolder>>(container, HoldersField);
        if (holders == null || !NeedsResync(player, holders))
        {
            return;
        }

        ResyncHolders(player, holders);
        MainFile.Logger.Info("Compacted potion slots and resynced belt UI");
    }

    private static void CompactPotionSlots(Player player)
    {
        if (PotionSlotsField?.GetValue(player) is not IList slots)
        {
            return;
        }

        var writeIndex = 0;

        for (var readIndex = 0; readIndex < slots.Count; readIndex++)
        {
            if (slots[readIndex] == null)
            {
                continue;
            }

            if (writeIndex != readIndex)
            {
                slots[writeIndex] = slots[readIndex];
                slots[readIndex] = null;
            }

            writeIndex++;
        }

        for (var i = writeIndex; i < slots.Count; i++)
        {
            slots[i] = null;
        }
    }

    private static bool NeedsResync(Player player, List<NPotionHolder> holders)
    {
        for (var i = 0; i < holders.Count; i++)
        {
            var expected = player.GetPotionAtSlotIndex(i);
            var actual = holders[i].HasPotion ? holders[i].Potion?.Model : null;
            if (!ReferenceEquals(expected, actual))
            {
                return true;
            }
        }

        return false;
    }

    private static void ResyncHolders(Player player, List<NPotionHolder> holders)
    {
        foreach (var holder in holders)
        {
            if (holder.HasPotion)
            {
                ClearHolderInstant(holder);
            }
        }

        for (var i = 0; i < holders.Count; i++)
        {
            var potion = player.GetPotionAtSlotIndex(i);
            if (potion == null)
            {
                continue;
            }

            var node = NPotion.Create(potion);
            if (node == null)
            {
                continue;
            }

            node.Position = new Vector2(-30f, -30f);
            holders[i].AddPotion(node);
        }
    }

    private static void ClearHolderInstant(NPotionHolder holder)
    {
        var potion = holder.Potion;
        if (potion == null)
        {
            return;
        }

        if (EmptyPotionTweenField?.GetValue(holder) is Tween tween)
        {
            tween.Kill();
            EmptyPotionTweenField.SetValue(holder, null);
        }

        HolderPotionProperty?.SetValue(holder, null);
        holder.RemoveChild(potion);
        potion.QueueFree();

        if (EmptyIconField?.GetValue(holder) is CanvasItem emptyIcon)
        {
            emptyIcon.Modulate = Colors.White;
        }
    }

    private static T? GetField<T>(object target, FieldInfo? field) where T : class
    {
        return field?.GetValue(target) as T;
    }
}
