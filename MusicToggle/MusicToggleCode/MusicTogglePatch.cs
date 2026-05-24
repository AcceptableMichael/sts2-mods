using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
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
    private static float _storedVolume = 0.5f;

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
            EnsureMusicToggleButton(____buttonContainer, ____settingsButton, ____compendiumButton);
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
            UpdateButtonLabel(button);
            button.Enable();
            FixControllerInput(____buttonContainer);
        }
    }

    private static NPauseMenuButton EnsureMusicToggleButton(
        Control buttonContainer,
        NPauseMenuButton settingsButton,
        NPauseMenuButton compendiumButton)
    {
        var button = buttonContainer.GetNodeOrNull<NPauseMenuButton>(ButtonName);
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
        // Match NBgmVolumeSlider.OnValueChanged: persist setting and apply to audio bus.
        SaveManager.Instance.SettingsSave.VolumeBgm = volume;
        NGame.Instance?.AudioManager?.SetBgmVol(volume);
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

    private static void UpdateButtonLabel(NPauseMenuButton button)
    {
        var isMuted = SaveManager.Instance.SettingsSave.VolumeBgm <= 0f;
        var label = isMuted ? "Unmute Music" : "Mute Music";
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
