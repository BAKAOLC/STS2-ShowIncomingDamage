using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2ShowIncomingDamage.Utils
{
    /// <summary>
    ///     Damage display service: manages combat health bar and player list damage labels.
    /// </summary>
    public static class DamageDisplayService
    {
        private const string BackdropNodeName = "__DamageBackdrop";
        private const float CharacterBackdropPaddingX = 10f;
        private const float CharacterBackdropPaddingY = 1f;
        private const float PlayerListBackdropPaddingX = 6f;
        private const float PlayerListBackdropPaddingY = 0f;
        private const float PlayerListXPadding = 4f;
        private const float PlayerListYOffsetNudge = 2f;
        private const float CharacterBarXPadding = 0f;
        private const float CharacterBarYOffsetNudge = 2f;

        private static readonly Dictionary<Creature, Label> CharacterLabels = [];
        private static readonly Dictionary<NMultiplayerPlayerState, RichTextLabel> PlayerListLabels = [];
        private static readonly Dictionary<Creature, DetailedDamagePanel> CharacterDetailPanels = [];
        private static readonly Dictionary<NMultiplayerPlayerState, DetailedDamagePanel> PlayerListDetailPanels = [];
        private static readonly HashSet<Control> HoverEventsSetup = [];

        private static readonly
            Dictionary<Control, (DetailedDamagePanel panel, Creature creature, CombatState combatState)> ActiveHovers =
                [];

        private static Font? _cachedFont;
        private static ulong _lastRecalcMs;

        public static void RefreshAll()
        {
            try
            {
                var combatState = CombatManager.Instance.DebugOnlyGetState();
                if (combatState == null || !CombatManager.Instance.IsInProgress)
                {
                    HideAll();
                    return;
                }

                if (!CombatManager.Instance.IsPlayPhase) return;

                RefreshCharacterLabels(combatState);
                RefreshPlayerListLabels(combatState);
                RefreshActiveHovers();
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to refresh damage display: {ex.Message}");
            }
        }

        public static void HideAll()
        {
            foreach (var label in CharacterLabels.Values.Where(GodotObject.IsInstanceValid))
                label.Visible = false;
            CharacterLabels.Clear();

            foreach (var label in PlayerListLabels.Values.Where(GodotObject.IsInstanceValid))
                label.Visible = false;

            foreach (var panel in CharacterDetailPanels.Values.Where(GodotObject.IsInstanceValid))
                panel.QueueFree();
            CharacterDetailPanels.Clear();

            foreach (var panel in PlayerListDetailPanels.Values.Where(GodotObject.IsInstanceValid))
                panel.QueueFree();
            PlayerListDetailPanels.Clear();

            HoverEventsSetup.Clear();
            ActiveHovers.Clear();
        }

        private static void RefreshCharacterLabels(CombatState combatState)
        {
            var combatRoom = NCombatRoom.Instance;
            if (combatRoom == null) return;

            var playerCreatures = combatState.PlayerCreatures
                .Where(c => c is { IsPet: false, IsPlayer: true, IsDead: false }).ToList();

            foreach (var playerCreature in playerCreatures)
            {
                var damageInfo = DamageCalculator.CalculateIncomingDamage(playerCreature, combatState);

                var displayColor = damageInfo.PlayerDamage > 0
                    ? new(1f, 0.3f, 0.3f)
                    : new Color(0.3f, 1f, 0.3f);
                ShowCharacterHealthBarLabel(playerCreature, damageInfo.PlayerDamage.ToString(),
                    displayColor, combatRoom);
                SetupCharacterDetailPanel(playerCreature, playerCreature, combatState, combatRoom);

                var petCreature = playerCreature.Pets?.FirstOrDefault(p => p is { IsDead: false });
                if (petCreature != null && damageInfo.PetDamage > 0)
                {
                    ShowCharacterHealthBarLabel(petCreature, damageInfo.PetDamage.ToString(),
                        new(1f, 0.6f, 0.2f), combatRoom);
                    SetupCharacterDetailPanel(petCreature, playerCreature, combatState, combatRoom);
                }
                else if (petCreature != null)
                {
                    HideCharacterLabel(petCreature);
                }
            }

            var shownCreatures = new HashSet<Creature>();
            foreach (var pc in playerCreatures)
            {
                shownCreatures.Add(pc);
                if (pc.Pets == null) continue;
                foreach (var pet in pc.Pets.Where(p => p is { IsDead: false }))
                    shownCreatures.Add(pet);
            }

            foreach (var creature in new List<Creature>(CharacterLabels.Keys).Where(c => !shownCreatures.Contains(c)))
            {
                HideCharacterLabel(creature);
                HideCharacterDetailPanel(creature);
            }
        }

        private static void RefreshPlayerListLabels(CombatState combatState)
        {
            var run = NRun.Instance;
            if (run?.GlobalUi?.MultiplayerPlayerContainer == null) return;
            var container = run.GlobalUi.MultiplayerPlayerContainer;

            for (var i = 0; i < container.GetChildCount(); i++)
            {
                if (container.GetChild(i) is not NMultiplayerPlayerState playerState) continue;

                var player = playerState.Player;
                if (player?.Creature == null || player.Creature.IsDead)
                {
                    HidePlayerListLabel(playerState);
                    HidePlayerListDetailPanel(playerState);
                    continue;
                }

                var damageInfo = DamageCalculator.CalculateIncomingDamage(player.Creature, combatState);

                var bbCode = FormatDamageDisplayBbCode(damageInfo);
                ShowPlayerListLabel(playerState, bbCode);
                SetupPlayerListDetailPanel(playerState, player.Creature, combatState);
            }

            foreach (var kv in new Dictionary<NMultiplayerPlayerState, RichTextLabel>(PlayerListLabels).Where(kv =>
                         !GodotObject.IsInstanceValid(kv.Key)))
                PlayerListLabels.Remove(kv.Key);
        }

        private static void RefreshActiveHovers()
        {
            foreach (var kv in ActiveHovers)
            {
                if (!GodotObject.IsInstanceValid(kv.Key) || !GodotObject.IsInstanceValid(kv.Value.panel))
                    continue;

                if (!kv.Value.panel.Visible)
                    continue;

                var freshInfo = DamageCalculator.CalculateIncomingDamage(kv.Value.creature, kv.Value.combatState, true);
                if (freshInfo.Details != null)
                    kv.Value.panel.ShowDetails(freshInfo.Details, freshInfo.HasPet, kv.Key);
            }
        }


        private static string FormatDamageDisplayBbCode(DamageCalculator.PlayerDamageInfo damageInfo)
        {
            const string playerDangerColor = "#FF4D4D";
            const string playerSafeColor = "#4DFF4D";
            const string petColor = "#FF9933";
            const string plusColor = "#D0D0D0";

            if (damageInfo is not { HasPet: true, PetDamage: > 0 })
                return damageInfo.PlayerDamage > 0
                    ? $"[color={playerDangerColor}]{damageInfo.PlayerDamage}[/color]"
                    : $"[color={playerSafeColor}]{damageInfo.PlayerDamage}[/color]";
            return damageInfo.PlayerDamage > 0
                ? $"[color={playerDangerColor}]{damageInfo.PlayerDamage}[/color][color={plusColor}]+[/color][color={petColor}]{damageInfo.PetDamage}[/color]"
                : $"[color={playerSafeColor}]0[/color][color={plusColor}]+[/color][color={petColor}]{damageInfo.PetDamage}[/color]";
        }

        private static void ShowCharacterHealthBarLabel(Creature creature, string text, Color color,
            NCombatRoom combatRoom)
        {
            if (!CharacterLabels.TryGetValue(creature, out var label) || !GodotObject.IsInstanceValid(label))
            {
                label = CreateDamageLabel(combatRoom);
                CharacterLabels[creature] = label;
            }

            var creatureNode = combatRoom.GetCreatureNode(creature);
            if (creatureNode == null || !TryGetCharacterHealthUi(creatureNode,
                    out var hpBarContainer))
            {
                HideCharacterLabel(creature);
                return;
            }

            if (label.GetParent() != hpBarContainer)
            {
                if (label.GetParent() == null)
                    hpBarContainer.AddChild(label);
                else
                    label.Reparent(hpBarContainer);
            }

            label.Text = text;
            label.AddThemeColorOverride("font_color", color);
            label.CustomMinimumSize = Vector2.Zero;
            label.ResetSize();

            var textSize = label.Size;
            label.CustomMinimumSize = new(
                textSize.X + CharacterBackdropPaddingX * 2f,
                textSize.Y + CharacterBackdropPaddingY * 2f);
            label.Size = label.CustomMinimumSize;

            UpdateBackdropForControl(label, CharacterBackdropPaddingX, CharacterBackdropPaddingY);
            label.Visible = true;
            Callable.From(() => UpdateBackdropForControl(label, CharacterBackdropPaddingX, CharacterBackdropPaddingY))
                .CallDeferred();

            PositionRightOfHealthBarLocal(label, hpBarContainer, CharacterBarXPadding, CharacterBarYOffsetNudge);
            Callable.From(() => PositionRightOfHealthBarLocal(label, hpBarContainer, CharacterBarXPadding,
                CharacterBarYOffsetNudge)).CallDeferred();
        }

        private static void HideCharacterLabel(Creature creature)
        {
            if (!CharacterLabels.TryGetValue(creature, out var label)) return;
            if (GodotObject.IsInstanceValid(label))
                label.Visible = false;
        }

        private static void SetupCharacterDetailPanel(Creature creature, Creature ownerCreature,
            CombatState combatState, NCombatRoom combatRoom)
        {
            try
            {
                if (!CharacterLabels.TryGetValue(creature, out var label) || !GodotObject.IsInstanceValid(label))
                    return;

                if (!CharacterDetailPanels.TryGetValue(creature, out var panel) || !GodotObject.IsInstanceValid(panel))
                {
                    panel = new();
                    panel.SetPreferUpward(true);
                    CharacterDetailPanels[creature] = panel;

                    var creatureNode = combatRoom.GetCreatureNode(creature);
                    if (creatureNode != null)
                        creatureNode.AddChild(panel);
                }

                if (HoverEventsSetup.Contains(label)) return;
                label.MouseFilter = Control.MouseFilterEnum.Pass;
                var capturedPanel = panel;
                label.MouseEntered += () =>
                {
                    ActiveHovers[label] = (capturedPanel, ownerCreature, combatState);
                    var freshInfo = DamageCalculator.CalculateIncomingDamage(ownerCreature, combatState, true);
                    if (freshInfo.Details != null)
                        OnLabelHoverStart(capturedPanel, label, freshInfo.Details, freshInfo.HasPet);
                };
                label.MouseExited += () =>
                {
                    ActiveHovers.Remove(label);
                    OnLabelHoverEnd(capturedPanel);
                };
                HoverEventsSetup.Add(label);
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to setup character detail panel: {ex.Message}");
            }
        }

        private static void HideCharacterDetailPanel(Creature creature)
        {
            if (!CharacterDetailPanels.TryGetValue(creature, out var panel)) return;
            if (GodotObject.IsInstanceValid(panel))
                panel.HidePanel();
        }

        private static void SetupPlayerListDetailPanel(NMultiplayerPlayerState playerState, Creature creature,
            CombatState combatState)
        {
            try
            {
                if (!PlayerListLabels.TryGetValue(playerState, out var label) || !GodotObject.IsInstanceValid(label))
                    return;

                if (!PlayerListDetailPanels.TryGetValue(playerState, out var panel) ||
                    !GodotObject.IsInstanceValid(panel))
                {
                    panel = new();
                    panel.SetPreferUpward(false);
                    PlayerListDetailPanels[playerState] = panel;
                    playerState.AddChild(panel);
                }

                if (HoverEventsSetup.Contains(label)) return;
                label.MouseFilter = Control.MouseFilterEnum.Pass;
                var capturedPanel = panel;
                label.MouseEntered += () =>
                {
                    ActiveHovers[label] = (capturedPanel, creature, combatState);
                    var freshInfo = DamageCalculator.CalculateIncomingDamage(creature, combatState, true);
                    if (freshInfo.Details != null)
                        OnLabelHoverStart(capturedPanel, label, freshInfo.Details, freshInfo.HasPet);
                };
                label.MouseExited += () =>
                {
                    ActiveHovers.Remove(label);
                    OnLabelHoverEnd(capturedPanel);
                };
                HoverEventsSetup.Add(label);
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to setup player list detail panel: {ex.Message}");
            }
        }

        private static void HidePlayerListDetailPanel(NMultiplayerPlayerState playerState)
        {
            if (!PlayerListDetailPanels.TryGetValue(playerState, out var panel)) return;
            if (GodotObject.IsInstanceValid(panel))
                panel.HidePanel();
        }

        private static void OnLabelHoverStart(DetailedDamagePanel panel, Control label,
            DamageCalculator.DetailedCalculation details, bool hasPet)
        {
            if (!GodotObject.IsInstanceValid(panel) || !GodotObject.IsInstanceValid(label)) return;

            panel.ShowDetails(details, hasPet, label);
        }

        private static void OnLabelHoverEnd(DetailedDamagePanel panel)
        {
            if (!GodotObject.IsInstanceValid(panel)) return;
            panel.HidePanel();
        }

        private static bool TryGetCharacterHealthUi(Node creatureNode, out Control hpBarContainer)
        {
            hpBarContainer = null!;

            var outerStateDisplay = creatureNode.GetNodeOrNull<Control>("%HealthBar");
            if (outerStateDisplay == null) return false;

            var healthBar = outerStateDisplay.GetNodeOrNull<Control>("%HealthBar") ?? outerStateDisplay;
            hpBarContainer = healthBar.GetNodeOrNull<Control>("%HpBarContainer") ?? healthBar;

            return GodotObject.IsInstanceValid(hpBarContainer);
        }

        private static void ShowPlayerListLabel(NMultiplayerPlayerState playerState, string bbCode)
        {
            if (!PlayerListLabels.TryGetValue(playerState, out var label) || !GodotObject.IsInstanceValid(label))
            {
                label = CreatePlayerListDamageLabel(playerState);
                playerState.AddChild(label);
                PlayerListLabels[playerState] = label;
            }

            label.Text = bbCode;
            label.ResetSize();

            var contentWidth = label.GetContentWidth();
            var contentHeight = label.GetContentHeight();
            label.CustomMinimumSize = new(contentWidth + PlayerListBackdropPaddingX * 2f, contentHeight);
            label.Size = label.CustomMinimumSize;

            label.Text = $"[center]{bbCode}[/center]";

            UpdateBackdropForControl(label, PlayerListBackdropPaddingX, PlayerListBackdropPaddingY);
            label.Visible = true;
            Callable.From(() => UpdateBackdropForControl(label, PlayerListBackdropPaddingX, PlayerListBackdropPaddingY))
                .CallDeferred();

            var healthBar = playerState.GetNodeOrNull<Control>("%HealthBar");
            if (healthBar == null) return;

            var hpBar = healthBar.GetNodeOrNull<Control>("%HpBarContainer");
            var bar = hpBar ?? healthBar;

            PositionRightOfHealthBar(label, healthBar, bar, PlayerListXPadding, PlayerListYOffsetNudge);
            Callable.From(() => PositionRightOfHealthBar(label, healthBar, bar, PlayerListXPadding,
                PlayerListYOffsetNudge)).CallDeferred();
        }

        private static void PositionRightOfHealthBar(Control label, Control healthBar, Control bar, float xPadding,
            float yOffsetNudge)
        {
            if (!GodotObject.IsInstanceValid(label) || !GodotObject.IsInstanceValid(healthBar) ||
                !GodotObject.IsInstanceValid(bar)) return;

            var barRightX = healthBar.Position.X + bar.Position.X + bar.Size.X;
            var desiredX = barRightX + xPadding;
            var barCenterY = healthBar.Position.Y + bar.Position.Y + bar.Size.Y * 0.5f;
            var desiredY = barCenterY - label.Size.Y * 0.5f - yOffsetNudge;
            label.Position = new(desiredX, desiredY);
        }

        private static void PositionRightOfHealthBarLocal(Control label, Control bar, float xPadding,
            float yOffsetNudge)
        {
            if (!GodotObject.IsInstanceValid(label) || !GodotObject.IsInstanceValid(bar)) return;

            var desiredX = bar.Size.X + xPadding;
            var barCenterY = bar.Size.Y * 0.5f;
            var desiredY = barCenterY - label.Size.Y * 0.5f - yOffsetNudge;
            label.Position = new(desiredX, desiredY);
        }

        private static void HidePlayerListLabel(NMultiplayerPlayerState playerState)
        {
            if (!PlayerListLabels.TryGetValue(playerState, out var label)) return;
            if (GodotObject.IsInstanceValid(label))
                label.Visible = false;
        }

        private static Label CreateDamageLabel(Node parentForFont)
        {
            var label = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Pass,
                Visible = false,
            };

            _cachedFont ??= FindGameFont(parentForFont);
            if (_cachedFont != null)
                label.AddThemeFontOverride("font", _cachedFont);

            label.AddThemeFontSizeOverride("font_size", 22);
            label.AddThemeConstantOverride("outline_size", 5);
            label.AddThemeColorOverride("font_outline_color", new(0, 0, 0));
            label.AddThemeColorOverride("font_color", new(1f, 0.3f, 0.3f));
            EnsureBackdrop(label);

            return label;
        }

        private static RichTextLabel CreatePlayerListDamageLabel(Node parentForFont)
        {
            var label = new RichTextLabel
            {
                BbcodeEnabled = true,
                AutowrapMode = TextServer.AutowrapMode.Off,
                FitContent = true,
                ScrollActive = false,
                MouseFilter = Control.MouseFilterEnum.Pass,
                Visible = false,
            };

            _cachedFont ??= FindGameFont(parentForFont);
            if (_cachedFont != null)
                label.AddThemeFontOverride("normal_font", _cachedFont);

            label.AddThemeFontSizeOverride("normal_font_size", 17);
            label.AddThemeConstantOverride("outline_size", 5);
            label.AddThemeColorOverride("font_outline_color", new(0, 0, 0));
            EnsureBackdrop(label);

            return label;
        }

        private static void EnsureBackdrop(Control parentLabel)
        {
            if (parentLabel.GetNodeOrNull<ColorRect>(BackdropNodeName) != null) return;

            var backdrop = new ColorRect
            {
                Name = BackdropNodeName,
                Color = new(0f, 0f, 0f, 0.72f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = true,
                ShowBehindParent = true,
            };

            parentLabel.AddChild(backdrop);
        }

        private static void UpdateBackdropForControl(Control label, float paddingX, float paddingY)
        {
            EnsureBackdrop(label);

            var backdrop = label.GetNodeOrNull<ColorRect>(BackdropNodeName);
            if (backdrop == null) return;

            backdrop.Position = new(-paddingX, -paddingY);
            backdrop.Size = new(
                Mathf.Max(1f, label.Size.X + paddingX * 2f),
                Mathf.Max(1f, label.Size.Y + paddingY * 2f));
        }

        private static Font? FindGameFont(Node root)
        {
            try
            {
                return FindFontRecursive(root, 5);
            }
            catch
            {
                return null;
            }
        }

        private static Font? FindFontRecursive(Node node, int depth)
        {
            if (depth <= 0) return null;

            foreach (var child in node.GetChildren())
            {
                if (child is Label label)
                {
                    var font = label.GetThemeFont("font");
                    if (font != null) return font;
                }

                var found = FindFontRecursive(child, depth - 1);
                if (found != null) return found;
            }

            return null;
        }

        public static bool ShouldPeriodicRefresh()
        {
            var now = Time.GetTicksMsec();
            if (now - _lastRecalcMs < 250) return false;
            _lastRecalcMs = now;
            return true;
        }
    }
}
