using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickPlayAgainButton.QuickPlayAgainButtonCode;

[HarmonyPatch]
public static class QuickPlayAgainButtonPatch
{
    private const string ButtonName = "PlayAgainButton";
    private const string ConnectedMeta = "QuickPlayAgainConnected";
    private const float HorizontalOffset = 320f;

    private const int DuplicateWithoutSignals = (int)(
        Node.DuplicateFlags.Groups |
        Node.DuplicateFlags.Scripts |
        Node.DuplicateFlags.UseInstantiation);

    [HarmonyPatch(typeof(NGameOverScreen), "_Ready")]
    public static class GameOverReadyPatch
    {
        [HarmonyPostfix]
        private static void AddPlayAgainButton(
            NReturnToMainMenuButton ____mainMenuButton,
            Control ____uiNode)
        {
            EnsurePlayAgainButton(____mainMenuButton, ____uiNode);
        }
    }

    [HarmonyPatch(typeof(NReturnToMainMenuButton), "OnEnable")]
    public static class MainMenuEnabledPatch
    {
        [HarmonyPostfix]
        private static void SyncPlayAgainButton(NReturnToMainMenuButton __instance)
        {
            if (__instance.Name == ButtonName)
            {
                return;
            }

            var screen = FindGameOverScreen(__instance);
            if (screen == null)
            {
                return;
            }

            var mainMenuButton = screen.GetNode<NReturnToMainMenuButton>("%MainMenuButton");
            if (__instance != mainMenuButton)
            {
                return;
            }

            UpdatePlayAgainVisibility(screen);
        }
    }

    [HarmonyPatch(typeof(NGameOverScreen), "OnMainMenuButtonPressed")]
    public static class MainMenuPressedPatch
    {
        [HarmonyPostfix]
        private static void DisablePlayAgainButton(NGameOverScreen __instance)
        {
            GetPlayAgainButton(__instance)?.Disable();
        }
    }

    private static NReturnToMainMenuButton EnsurePlayAgainButton(
        NReturnToMainMenuButton mainMenuButton,
        Control uiNode)
    {
        var existing = uiNode.GetNodeOrNull<NReturnToMainMenuButton>(ButtonName);
        if (existing != null)
        {
            return existing;
        }

        var playAgainButton = (NReturnToMainMenuButton)mainMenuButton.Duplicate(DuplicateWithoutSignals);
        playAgainButton.Name = ButtonName;

        var image = playAgainButton.GetNode<TextureRect>("Image");
        image.Material = (ShaderMaterial)image.Material.Duplicate();
        SetPrivateField(playAgainButton, "_hsv", image.Material);

        uiNode.AddChild(playAgainButton);
        uiNode.MoveChild(playAgainButton, mainMenuButton.GetIndex());

        playAgainButton.GetNode<MegaLabel>("Label").SetTextAutoSize("Play Again");
        AdjustButtonPairPosition(mainMenuButton, playAgainButton);
        EnsureReleasedHandler(playAgainButton);
        playAgainButton.Disable();

        MainFile.Logger.Info("Added Play again button to game over screen");
        return playAgainButton;
    }

    private static void AdjustButtonPairPosition(
        NReturnToMainMenuButton mainMenuButton,
        NReturnToMainMenuButton playAgainButton)
    {
        var center = GetPrivateFieldValue<Vector2>(mainMenuButton, "_showPosition");
        var halfOffset = HorizontalOffset * 0.5f;
        SetButtonShowPosition(mainMenuButton, center + new Vector2(halfOffset, 0f));
        SetButtonShowPosition(playAgainButton, center - new Vector2(halfOffset, 0f));
    }

    private static void SetButtonShowPosition(NReturnToMainMenuButton button, Vector2 showPosition)
    {
        SetPrivateField(button, "_showPosition", showPosition);
        button.Position = showPosition;
    }

    private static void EnsureReleasedHandler(NReturnToMainMenuButton button)
    {
        if (button.HasMeta(ConnectedMeta))
        {
            return;
        }

        button.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(OnPlayAgainButtonReleased));
        button.SetMeta(ConnectedMeta, true);
    }

    private static void OnPlayAgainButtonReleased(NButton _)
    {
        var screen = FindGameOverScreen(_);
        if (screen == null)
        {
            return;
        }

        var runState = GetPrivateField<RunState>(screen, "_runState");
        var localPlayer = GetPrivateField<Player>(screen, "_localPlayer");
        if (runState == null || localPlayer == null)
        {
            return;
        }

        TaskHelper.RunSafely(PlayAgainAsync(screen, runState, localPlayer));
    }

    private static async Task PlayAgainAsync(NGameOverScreen screen, RunState runState, Player localPlayer)
    {
        if (!ShouldShowPlayAgain(runState))
        {
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Host)
        {
            RunManager.Instance.NetService.Disconnect(NetError.QuitGameOver);
        }

        var playAgainButton = GetPlayAgainButton(screen);
        var mainMenuButton = screen.GetNode<NReturnToMainMenuButton>("%MainMenuButton");
        playAgainButton?.Disable();
        mainMenuButton.Disable();

        var character = localPlayer.Character;
        var ascension = runState.AscensionLevel;
        var gameMode = runState.GameMode;

        NAudioManager.Instance?.StopMusic();
        SfxCmd.Play(character.CharacterTransitionSfx);
        await NGame.Instance!.Transition.FadeOut(0.8f, character.CharacterSelectTransitionPath);

        RunManager.Instance.CleanUp();

        var seed = SeedHelper.GetRandomSeed();
        var rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed));
        var unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        var acts = ActModel.GetRandomList(rng, unlockState, isMultiplayer: false).ToList();

        MainFile.Logger.Info(
            $"Starting new run with {character.Id.Entry} at ascension {ascension} (seed: {seed})");

        await NGame.Instance.StartNewSingleplayerRun(
            character,
            shouldSave: true,
            acts,
            Array.Empty<ModifierModel>(),
            seed,
            gameMode,
            ascension);
    }

    private static void UpdatePlayAgainVisibility(NGameOverScreen screen)
    {
        var playAgainButton = GetPlayAgainButton(screen);
        if (playAgainButton == null)
        {
            return;
        }

        var runState = GetPrivateField<RunState>(screen, "_runState");
        if (runState == null || !ShouldShowPlayAgain(runState))
        {
            playAgainButton.Disable();
            return;
        }

        var mainMenuButton = screen.GetNode<NReturnToMainMenuButton>("%MainMenuButton");
        playAgainButton.Enable();
        playAgainButton.FocusNeighborRight = mainMenuButton.GetPath();
        mainMenuButton.FocusNeighborLeft = playAgainButton.GetPath();
    }

    private static bool ShouldShowPlayAgain(RunState runState)
    {
        return RunManager.Instance.NetService.Type == NetGameType.Singleplayer
            && runState.GameMode != GameMode.Daily;
    }

    private static NReturnToMainMenuButton? GetPlayAgainButton(NGameOverScreen screen)
    {
        return screen.GetNode<Control>("%Ui").GetNodeOrNull<NReturnToMainMenuButton>(ButtonName);
    }

    private static NGameOverScreen? FindGameOverScreen(Node node)
    {
        var current = node;
        while (current != null)
        {
            if (current is NGameOverScreen screen)
            {
                return screen;
            }

            current = current.GetParent();
        }

        return null;
    }

    private static T GetPrivateFieldValue<T>(object target, string fieldName) where T : struct
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(target) is T value ? value : default;
    }

    private static T? GetPrivateField<T>(object target, string fieldName) where T : class
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(target) as T;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }
}
