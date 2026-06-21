using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Saves;

namespace MusicToggle.MusicToggleCode;

[HarmonyPatch]
public static class MusicTogglePatch
{
    private const string ButtonName = "MusicToggleButton";
    private const string ConnectedMeta = "MusicToggleConnected";
    private const string GameOverMusic = "event:/temp/sfx/game_over";
    private static float _storedVolume = 0.5f;
    private static bool _isBgmMuted;
    private static Control? _pauseMenuButtonContainer;

    // Exclude DuplicateFlags.Signals so we don't inherit the Settings button's click handler.
    private const int DuplicateWithoutSignals = (int)(
        Node.DuplicateFlags.Groups |
        Node.DuplicateFlags.Scripts |
        Node.DuplicateFlags.UseInstantiation);

    [HarmonyPatch(typeof(NPauseMenu), "_Ready")]
    public static class PauseMenuButtonPatch
    {
        [HarmonyPostfix]
        private static void AddMusicToggleButton(
            Control ____buttonContainer,
            NPauseMenuButton ____settingsButton,
            NPauseMenuButton ____compendiumButton)
        {
            SyncMuteStateFromSettings();
            EnsureMusicToggleButton(____buttonContainer, ____settingsButton, ____compendiumButton);
        }
    }

    [HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol))]
    public static class SetBgmVolPatch
    {
        [HarmonyPostfix]
        private static void SyncMuteStateAndLabel(float volume)
        {
            _isBgmMuted = volume <= 0f;
            if (volume > 0f)
            {
                _storedVolume = volume;
            }

            RefreshMusicToggleButtonLabel();
        }
    }

    [HarmonyPatch(typeof(NSettingsScreen), "OnSubmenuClosed")]
    public static class SettingsClosedPatch
    {
        [HarmonyPostfix]
        private static void RefreshMusicToggleButton()
        {
            SyncMuteStateFromSettings();
            RefreshMusicToggleButtonLabel();
        }
    }

    [HarmonyPatch(typeof(NPauseMenu), "Initialize")]
    public static class PauseMenuInitializePatch
    {
        [HarmonyPostfix]
        private static void RefreshMusicToggleButton(
            Control ____buttonContainer,
            NPauseMenuButton ____settingsButton,
            NPauseMenuButton ____compendiumButton)
        {
            var button = EnsureMusicToggleButton(____buttonContainer, ____settingsButton, ____compendiumButton);
            SyncMuteStateFromSettings();
            UpdateButtonLabel(button);
            button.Enable();
            FixControllerInput(____buttonContainer);
        }
    }

    [HarmonyPatch(typeof(NPauseMenu), "CloseToMenu")]
    public static class PauseMenuCloseToMenuPatch
    {
        [HarmonyPrefix]
        private static void DisableMusicToggleButton(Control ____buttonContainer)
        {
            GetMusicToggleButton(____buttonContainer)?.Disable();
        }
    }

    [HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic))]
    public static class PlayMusicPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string music)
        {
            if (ShouldSkipRunEndMusic(music))
            {
                MainFile.Logger.Info($"Skipping run end music '{music}' because BGM is muted");
                return false;
            }

            return true;
        }
    }

    private static bool ShouldSkipRunEndMusic(string music) =>
        IsRunEndMusic(music) && IsBgmMuted();

    private static bool IsRunEndMusic(string music) =>
        !string.IsNullOrEmpty(music)
        && (music == GameOverMusic || music.Contains("game_over", StringComparison.Ordinal));

    private static bool IsBgmMuted() =>
        _isBgmMuted || SaveManager.Instance.SettingsSave.VolumeBgm <= 0f;

    private static void SyncMuteStateFromSettings()
    {
        _isBgmMuted = SaveManager.Instance.SettingsSave.VolumeBgm <= 0f;
        if (!_isBgmMuted && _storedVolume <= 0f)
        {
            _storedVolume = SaveManager.Instance.SettingsSave.VolumeBgm;
        }
    }

    private static NPauseMenuButton EnsureMusicToggleButton(
        Control buttonContainer,
        NPauseMenuButton settingsButton,
        NPauseMenuButton compendiumButton)
    {
        _pauseMenuButtonContainer = buttonContainer;
        var button = GetMusicToggleButton(buttonContainer);
        if (button == null)
        {
            button = (NPauseMenuButton)settingsButton.Duplicate(DuplicateWithoutSignals);
            button.Name = ButtonName;

            var image = button.GetNode<TextureRect>("ButtonImage");
            image.Material = (ShaderMaterial)image.Material.Duplicate();
            SetPrivateField(button, "_hsv", image.Material);

            buttonContainer.AddChild(button);
            buttonContainer.MoveChild(button, compendiumButton.GetIndex());

            MainFile.Logger.Info("Added Music Toggle button to pause menu");
        }

        EnsureReleasedHandler(button);
        UpdateButtonLabel(button);
        button.Enable();
        FixControllerInput(buttonContainer);
        return button;
    }

    private static void EnsureReleasedHandler(NPauseMenuButton button)
    {
        if (button.HasMeta(ConnectedMeta))
        {
            return;
        }

        button.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(OnMusicToggleButtonReleased));
        button.SetMeta(ConnectedMeta, true);
    }

    private static void OnMusicToggleButtonReleased(NButton _)
    {
        var currentVolume = SaveManager.Instance.SettingsSave.VolumeBgm;

        if (currentVolume > 0f)
        {
            _storedVolume = currentVolume;
            ApplyBgmVolume(0f);
            MainFile.Logger.Info($"Muted background music (stored volume {_storedVolume:F2})");
        }
        else
        {
            ApplyBgmVolume(_storedVolume);
            MainFile.Logger.Info($"Restored background music to {_storedVolume:F2}");
        }

        if (_.GetParent<Control>()?.GetNodeOrNull<NPauseMenuButton>(ButtonName) is { } button)
        {
            UpdateButtonLabel(button);
        }
    }

    private static void ApplyBgmVolume(float volume)
    {
        _isBgmMuted = volume <= 0f;

        // Match NBgmVolumeSlider.OnValueChanged: persist setting and apply to audio bus.
        SaveManager.Instance.SettingsSave.VolumeBgm = volume;
        NGame.Instance?.AudioManager?.SetBgmVol(volume);
        if (_isBgmMuted)
        {
            NGame.Instance?.AudioManager?.StopMusic();
        }

        SyncBgmVolumeSliders(volume);
    }

    private static void SyncBgmVolumeSliders(float volume)
    {
        var root = NGame.Instance?.GetTree()?.Root;
        if (root == null)
        {
            return;
        }

        SyncBgmVolumeSlidersRecursive(root, volume);
    }

    private static void SyncBgmVolumeSlidersRecursive(Node node, float volume)
    {
        if (node is NBgmVolumeSlider)
        {
            var slider = GetProtectedField<NSlider>(node, "_slider");
            var valueLabel = GetProtectedField<MegaLabel>(node, "_valueLabel");
            var sliderPercent = volume * 100f;

            slider?.SetValueWithoutAnimation(sliderPercent);
            valueLabel?.SetTextAutoSize($"{sliderPercent:0}%");
        }

        foreach (var child in node.GetChildren())
        {
            SyncBgmVolumeSlidersRecursive(child, volume);
        }
    }

    private static NPauseMenuButton? GetMusicToggleButton(Control buttonContainer)
    {
        return buttonContainer.GetNodeOrNull<NPauseMenuButton>(ButtonName);
    }

    private static void RefreshMusicToggleButtonLabel()
    {
        if (_pauseMenuButtonContainer != null)
        {
            var button = GetMusicToggleButton(_pauseMenuButtonContainer);
            if (button != null)
            {
                UpdateButtonLabel(button);
                return;
            }
        }

        var root = NGame.Instance?.GetTree()?.Root;
        if (root == null)
        {
            return;
        }

        RefreshMusicToggleButtonLabelRecursive(root);
    }

    private static void RefreshMusicToggleButtonLabelRecursive(Node node)
    {
        if (node is NPauseMenuButton button && (string)button.Name == ButtonName)
        {
            UpdateButtonLabel(button);
            return;
        }

        foreach (var child in node.GetChildren())
        {
            RefreshMusicToggleButtonLabelRecursive(child);
        }
    }

    private static void UpdateButtonLabel(NPauseMenuButton button)
    {
        var isMuted = SaveManager.Instance.SettingsSave.VolumeBgm <= 0f;
        var label = isMuted ? "Play Music" : "Mute Music";
        button.GetNode<MegaLabel>("Label").SetTextAutoSize(label);
    }

    private static void FixControllerInput(Control buttonContainer)
    {
        var buttons = buttonContainer
            .GetChildren()
            .OfType<NPauseMenuButton>()
            .Where(b => b is { Visible: true, IsEnabled: true })
            .ToList();

        for (var i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            var path = btn.GetPath();
            btn.FocusNeighborLeft = path;
            btn.FocusNeighborRight = path;
            btn.FocusNeighborTop = i > 0 ? buttons[i - 1].GetPath() : path;
            btn.FocusNeighborBottom = i < buttons.Count - 1 ? buttons[i + 1].GetPath() : path;
        }
    }

    private static T? GetProtectedField<T>(object target, string fieldName) where T : class
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
