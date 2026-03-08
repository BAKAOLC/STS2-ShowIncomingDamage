using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace STS2ShowIncomingDamage.Utils
{
    public partial class DetailedDamagePanel : PanelContainer
    {
        private const int PanelZIndex = 1000;
        private const int MaxPanelWidth = 500;
        private const int IconSize = 18;
        private const int RowHeight = 22;
        private const int IconColumnWidth = 24;
        private const int NumberColumnWidth = 24;
        private const int SourceColumnWidth = 120;
        private const int FontSize = 12;
        private const int SmallFontSize = 11;

        private static string? _cachedBlockKeyword;

        private VBoxContainer? _contentContainer;
        private bool _preferUpward;

        private static string BlockKeyword =>
            _cachedBlockKeyword ??= ResolveBlockKeyword();

        private static string ResolveBlockKeyword()
        {
            try
            {
                var loc = new LocString("intents", "DEFEND.title");
                var text = loc.GetFormattedText();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch
            {
                // ignored
            }

            return "Block";
        }

        public override void _Ready()
        {
            base._Ready();
            InitializePanel();
        }

        private void InitializePanel()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex = PanelZIndex;
            ClipContents = false;
            SizeFlagsVertical = SizeFlags.ShrinkBegin;
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

            var styleBox = new StyleBoxFlat
            {
                BgColor = PanelColors.Background,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                BorderColor = PanelColors.Border,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 10,
                ContentMarginRight = 10,
                ContentMarginTop = 8,
                ContentMarginBottom = 8,
            };

            AddThemeStyleboxOverride("panel", styleBox);

            _contentContainer = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            _contentContainer.AddThemeConstantOverride("separation", 2);

            AddChild(_contentContainer);
        }

        public void SetPreferUpward(bool preferUpward)
        {
            _preferUpward = preferUpward;
        }

        public void ShowDetails(DamageCalculator.DetailedCalculation details, bool hasPet, Control anchorControl)
        {
            if (_contentContainer == null) return;

            ClearContent();
            BuildContent(details, hasPet);

            Visible = true;
            CustomMinimumSize = Vector2.Zero;
            Size = Vector2.Zero;
            ResetSize();
            Callable.From(() => AdjustPosition(anchorControl)).CallDeferred();
        }

        public void HidePanel()
        {
            Visible = false;
        }

        private void ClearContent()
        {
            if (_contentContainer == null) return;
            foreach (var child in _contentContainer.GetChildren())
            {
                if (child == null) continue;
                _contentContainer.RemoveChild(child);
                child.QueueFree();
            }
        }

        private void BuildContent(DamageCalculator.DetailedCalculation details, bool hasPet)
        {
            if (_contentContainer == null) return;

            var hasDamageSteps = false;
            foreach (var step in details.Steps)
                if (step.IsBlock)
                {
                    AddBlockStep(step);
                }
                else
                {
                    hasDamageSteps = true;
                    AddDamageStep(step, hasPet);
                }

            if (!hasDamageSteps)
                AddNoDamageMessage();
        }

        private void AddNoDamageMessage()
        {
            var row = CreateRow();

            var icon = CreateIcon(ImageHelper.GetImagePath("atlases/intent_atlas.sprites/intent_defend.tres"),
                PanelColors.Safe, IconSize);
            var iconWrap = CreateFixedColumn(IconColumnWidth);
            iconWrap.AddChild(icon);
            row.AddChild(iconWrap);

            var label = CreateLabel("Safe", FontSize, PanelColors.Safe);
            row.AddChild(label);
            _contentContainer?.AddChild(row);
        }

        private void AddBlockStep(DamageCalculator.ExecutionStep step)
        {
            var row = CreateRow();

            var icon = CreateIconFromSource(step.SourceObject, step.SourceType);
            var iconWrap = CreateFixedColumn(IconColumnWidth);
            iconWrap.AddChild(icon);
            row.AddChild(iconWrap);

            var numWrap = CreateFixedColumn(NumberColumnWidth);
            numWrap.AddChild(CreateLabel($"{step.StepNumber}.", SmallFontSize, PanelColors.TextDim));
            row.AddChild(numWrap);

            var srcWrap = CreateFixedColumn(SourceColumnWidth);
            srcWrap.AddChild(CreateLabel(step.Source, FontSize, PanelColors.Text));
            row.AddChild(srcWrap);

            var resultWrap = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            resultWrap.AddThemeConstantOverride("separation", 4);
            resultWrap.AddChild(CreateLabel($"+{step.Amount}", FontSize, DetermineBlockColor(step.SourceType)));
            resultWrap.AddChild(CreateLabel(BlockKeyword, SmallFontSize, PanelColors.TextDim));
            resultWrap.AddChild(CreateLabel($"[{step.BlockAfter}]", SmallFontSize, PanelColors.BlockRemaining));
            row.AddChild(resultWrap);

            _contentContainer?.AddChild(row);
        }

        private void AddDamageStep(DamageCalculator.ExecutionStep step, bool hasPet)
        {
            var row = CreateRow();

            var icon = CreateIconFromSource(step.SourceObject, step.SourceType);
            var iconWrap = CreateFixedColumn(IconColumnWidth);
            iconWrap.AddChild(icon);
            row.AddChild(iconWrap);

            var numWrap = CreateFixedColumn(NumberColumnWidth);
            numWrap.AddChild(CreateLabel($"{step.StepNumber}.", SmallFontSize, PanelColors.TextDim));
            row.AddChild(numWrap);

            var srcWrap = CreateFixedColumn(SourceColumnWidth);
            var sourceColor = DetermineColorByType(step.SourceType);
            srcWrap.AddChild(CreateLabel(step.Source, FontSize, sourceColor));
            row.AddChild(srcWrap);

            var resultWrap = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            resultWrap.AddThemeConstantOverride("separation", 3);

            resultWrap.AddChild(CreateLabel(step.Amount.ToString(), FontSize, PanelColors.EnemyDamage));

            if (step.BlockUsed > 0)
                resultWrap.AddChild(CreateLabel($"-{step.BlockUsed}", FontSize, PanelColors.BlockUsed));

            if (hasPet && step.PetDamage > 0)
                resultWrap.AddChild(CreateLabel($"-{step.PetDamage}pet", FontSize, PanelColors.PetDamage));

            resultWrap.AddChild(CreateLabel("→", SmallFontSize, PanelColors.TextDim));

            resultWrap.AddChild(step.PlayerDamage > 0
                ? CreateLabel($"-{step.PlayerDamage}HP", FontSize, PanelColors.PlayerDamage)
                : CreateLabel("0", FontSize, PanelColors.Safe));

            resultWrap.AddChild(CreateLabel($"[{step.BlockAfter}]", SmallFontSize, PanelColors.BlockRemaining));

            row.AddChild(resultWrap);

            _contentContainer?.AddChild(row);
        }

        private static HBoxContainer CreateRow()
        {
            var row = new HBoxContainer
            {
                CustomMinimumSize = new(0, RowHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            row.AddThemeConstantOverride("separation", 0);
            return row;
        }

        private static HBoxContainer CreateFixedColumn(int width)
        {
            var col = new HBoxContainer
            {
                CustomMinimumSize = new(width, 0),
                SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            };
            col.AddThemeConstantOverride("separation", 0);
            return col;
        }

        private static Control CreateIconFromSource(object? sourceObject, string sourceType)
        {
            var fallbackColor = DetermineColorByType(sourceType);

            if (sourceObject == null)
            {
                if (sourceType == "Block")
                    return CreateIcon(ImageHelper.GetImagePath("atlases/intent_atlas.sprites/intent_defend.tres"),
                        fallbackColor, IconSize);
                return CreateIconPlaceholder(fallbackColor);
            }

            try
            {
                if (sourceObject is ValueTuple<Creature, AttackIntent, Creature> enemyTuple)
                {
                    try
                    {
                        var texture = enemyTuple.Item2.GetTexture([enemyTuple.Item3], enemyTuple.Item1);
                        if (texture != null)
                            return new TextureRect
                            {
                                Texture = texture,
                                CustomMinimumSize = new(IconSize, IconSize),
                                Size = new(IconSize, IconSize),
                                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                            };
                    }
                    catch
                    {
                        // fall through to path-based icon
                    }

                    return CreateIcon(
                        ImageHelper.GetImagePath("atlases/intent_atlas.sprites/attack/intent_attack_1.tres"),
                        fallbackColor, IconSize);
                }

                var iconPath = sourceObject switch
                {
                    PowerModel power => power.PackedIconPath,
                    CardModel card => card.PortraitPath,
                    OrbModel orb => ImageHelper.GetImagePath($"orbs/{orb.Id.Entry.ToLowerInvariant()}.png"),
                    _ => null,
                };

                return CreateIcon(iconPath, fallbackColor, IconSize);
            }
            catch
            {
                return CreateIconPlaceholder(fallbackColor);
            }
        }

        private static Color DetermineColorByType(string sourceType)
        {
            return sourceType switch
            {
                "Card" => PanelColors.CardDamage,
                "Power" => PanelColors.PowerDamage,
                "Enemy" => PanelColors.EnemyDamage,
                "Orb" => PanelColors.OrbBlock,
                "Block" => PanelColors.BlockUsed,
                _ => PanelColors.Text,
            };
        }

        private static Color DetermineBlockColor(string sourceType)
        {
            return sourceType switch
            {
                "Power" => PanelColors.PowerBlock,
                "Orb" => PanelColors.OrbBlock,
                _ => PanelColors.BlockUsed,
            };
        }

        private static ColorRect CreateIconPlaceholder(Color color)
        {
            return new()
            {
                CustomMinimumSize = new(IconSize, IconSize),
                Color = color,
            };
        }

        private static Control CreateIcon(string? iconPath, Color fallbackColor, int size)
        {
            if (string.IsNullOrEmpty(iconPath))
                return CreateIconPlaceholder(fallbackColor);

            try
            {
                var texture = PreloadManager.Cache.GetTexture2D(iconPath);
                if (texture == null)
                    return CreateIconPlaceholder(fallbackColor);

                return new TextureRect
                {
                    Texture = texture,
                    CustomMinimumSize = new(size, size),
                    Size = new(size, size),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                };
            }
            catch
            {
                return CreateIconPlaceholder(fallbackColor);
            }
        }

        private static Label CreateLabel(string text, int fontSize, Color color)
        {
            var label = new Label
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        private void AdjustPosition(Control anchorControl)
        {
            if (!IsInstanceValid(anchorControl)) return;

            CustomMinimumSize = Vector2.Zero;
            Size = Vector2.Zero;
            ResetSize();

            var viewport = GetViewport();
            if (viewport == null) return;

            var viewportSize = viewport.GetVisibleRect().Size;
            var anchorGlobalPos = anchorControl.GlobalPosition;
            var anchorSize = anchorControl.Size;

            var panelSize = Size;
            if (panelSize.X > MaxPanelWidth)
            {
                CustomMinimumSize = new(MaxPanelWidth, 0);
                ResetSize();
                panelSize = Size;
            }

            var preferredX = anchorGlobalPos.X + anchorSize.X + 8;
            if (preferredX + panelSize.X > viewportSize.X)
                preferredX = anchorGlobalPos.X - panelSize.X - 8;
            if (preferredX < 0)
                preferredX = 4;

            float preferredY;
            if (_preferUpward)
            {
                preferredY = anchorGlobalPos.Y + anchorSize.Y - panelSize.Y;
            }
            else
            {
                preferredY = anchorGlobalPos.Y;
                if (preferredY + panelSize.Y > viewportSize.Y)
                    preferredY = viewportSize.Y - panelSize.Y - 4;
            }

            if (preferredY < 0)
                preferredY = 4;

            GlobalPosition = new(preferredX, preferredY);
        }

        public static class PanelColors
        {
            public static readonly Color Background = new(0.08f, 0.08f, 0.08f, 0.96f);
            public static readonly Color Border = new(0.4f, 0.4f, 0.4f);
            public static readonly Color Text = new(0.85f, 0.85f, 0.85f);
            public static readonly Color TextDim = new(0.6f, 0.6f, 0.6f);

            public static readonly Color EnemyDamage = new(1f, 0.42f, 0.42f);
            public static readonly Color PowerDamage = new(1f, 0.67f, 0.29f);
            public static readonly Color CardDamage = new(1f, 0.85f, 0.24f);
            public static readonly Color OrbBlock = new(0.42f, 0.81f, 0.5f);
            public static readonly Color PowerBlock = new(0.31f, 0.8f, 0.77f);
            public static readonly Color BlockUsed = new(0.58f, 0.88f, 0.83f);
            public static readonly Color BlockRemaining = new(0.45f, 0.65f, 0.75f);
            public static readonly Color PetDamage = new(1f, 0.62f, 0.26f);
            public static readonly Color PlayerDamage = new(0.93f, 0.35f, 0.44f);
            public static readonly Color Safe = new(0.66f, 0.9f, 0.81f);
        }
    }
}
