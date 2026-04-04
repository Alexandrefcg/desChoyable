using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using DestroyChecker.Core.Models;
using DestroyChecker.Core.Services;
using Microsoft.Xna.Framework;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace DestroyChecker.BlishModule
{
    [Export(typeof(Module))]
    public class DestroyCheckerModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<DestroyCheckerModule>();

        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;

        private InventoryAnalyzer? _analyzer;
        private CornerIcon? _cornerIcon;
        private StandardWindow? _mainWindow;
        private FlowPanel? _contentPanel;
        private Label? _statusLabel;
        private double _scanTimer;
        private string? _lastCharacterName;

        private string? _lastResultsHash;

        private const double ScanIntervalMs = 60_000;
        private const int WindowWidth = 580;
        private const int WindowHeight = 660;

        [ImportingConstructor]
        public DestroyCheckerModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters)
        {
        }

        protected override void DefineSettings(SettingCollection settings)
        {
        }

        protected override Task LoadAsync()
        {
            _analyzer = new InventoryAnalyzer(Gw2ApiManager);

            var windowBg = AsyncTexture2D.FromAssetId(155997);

            _mainWindow = new StandardWindow(
                windowBg,
                new Rectangle(25, 26, WindowWidth - 20, WindowHeight - 20),
                new Rectangle(40, 50, WindowWidth - 60, WindowHeight - 70))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "DesChoyable",
                Subtitle = "Inventory Analyzer",
                Location = new Point(300, 300),
                SavesPosition = true,
                Id = $"{nameof(DestroyCheckerModule)}_MainWindow_v2"
            };

            _contentPanel = new FlowPanel
            {
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.Fill,
                Parent = _mainWindow,
                CanScroll = true,
            };

            _statusLabel = new Label
            {
                Text = "Waiting for character data...",
                TextColor = Color.LightGray,
                Font = GameService.Content.DefaultFont16,
                AutoSizeHeight = true,
                AutoSizeWidth = true,
                Parent = _contentPanel
            };

            _cornerIcon = new CornerIcon
            {
                IconName = "DesChoyable",
                Icon = AsyncTexture2D.FromAssetId(157085),
                BasicTooltipText = "DesChoyable — Click to toggle",
                Priority = 1284759321,
                Parent = GameService.Graphics.SpriteScreen
            };

            _cornerIcon.Click += (s, e) => _mainWindow.ToggleWindow();

            return Task.CompletedTask;
        }

        protected override void Update(GameTime gameTime)
        {
            _scanTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

            var currentChar = GameService.Gw2Mumble.PlayerCharacter.Name;

            // Auto-scan on character change or on timer
            bool characterChanged = !string.IsNullOrEmpty(currentChar)
                                    && currentChar != _lastCharacterName;

            if (characterChanged || _scanTimer > ScanIntervalMs)
            {
                _scanTimer = 0;

                if (!string.IsNullOrEmpty(currentChar) && _analyzer != null && !_analyzer.IsLoading)
                {
                    _lastCharacterName = currentChar;
                    Task.Run(RunScanAsync);
                }
            }
        }

        private async Task RunScanAsync()
        {
            if (_analyzer == null || _statusLabel == null) return;

            _statusLabel.Text = "Scanning inventory...";

            var results = await _analyzer.AnalyzeCurrentCharacterAsync();

            UpdateUI(results);
        }

        private void UpdateUI(List<ItemInfo> items)
        {
            if (_contentPanel == null || _statusLabel == null) return;

            // Build a hash to detect if results actually changed
            var resultsHash = string.Join("|", items.Select(i => $"{i.Id}:{i.TotalCount}:{i.Safety}"));
            if (resultsHash == _lastResultsHash)
            {
                _statusLabel.Text = $"Scanned: {_analyzer?.LastScannedCharacter}"
                    + $" — {items.Count} items (no changes)";
                return;
            }
            _lastResultsHash = resultsHash;

            // Clear existing content except status label
            var toRemove = _contentPanel.Children
                .Where(c => c != _statusLabel)
                .ToList();
            foreach (var c in toRemove)
                c.Dispose();

            if (items.Count == 0)
            {
                _statusLabel.Text = _analyzer?.LastScannedCharacter != null
                    ? $"No Trophy/Gizmo items found on {_analyzer.LastScannedCharacter}."
                    : "No items to analyze.";
                return;
            }

            var safe = items.Where(i => i.Safety == ItemSafety.Safe).ToList();
            var check = items.Where(i => i.Safety == ItemSafety.Check).ToList();
            var keep = items.Where(i => i.Safety == ItemSafety.Keep).ToList();

            _statusLabel.Text = $"Scanned: {_analyzer?.LastScannedCharacter}"
                + $" — {items.Count} items ({safe.Count} safe, {check.Count} check, {keep.Count} keep)";

            CreateCategoryPanel("Safe to Destroy", safe, new Color(80, 200, 80));
            CreateCategoryPanel("Check Before Destroying", check, new Color(220, 200, 60));
            CreateCategoryPanel("Do Not Destroy", keep, new Color(220, 80, 80));
        }

        private const int IconSize = 32;

        private void CreateCategoryPanel(string title, List<ItemInfo> items, Color titleColor)
        {
            if (_contentPanel == null || items.Count == 0) return;

            var section = new FlowPanel
            {
                Title = $"{title} ({items.Count})",
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.AutoSize,
                CanCollapse = true,
                ShowBorder = true,
                Parent = _contentPanel,
                OuterControlPadding = new Vector2(4, 4),
                ControlPadding = new Vector2(0, 4),
            };

            foreach (var item in items)
            {
                var text = $"{item.Name} ({item.Rarity})";
                if (item.TotalCount > 1) text += $" x{item.TotalCount}";

                var detail = item.SafetyReason;
                if (item.VendorValue > 0)
                    detail += $" | Sell: {ItemClassifier.FormatCoins(item.VendorValue)}";
                if (item.UsedInRecipes)
                    detail += $" | {item.RecipeCount} recipe(s)";
                if (item.BelongsToCollection)
                {
                    var status = item.AllCollectionsCompleted ? "completed" : "incomplete";
                    detail += $" | Collection: {string.Join(", ", item.CollectionNames)} ({status})";
                }

                // Row: icon on the left, text on the right
                var rowPanel = new FlowPanel
                {
                    FlowDirection = ControlFlowDirection.SingleLeftToRight,
                    WidthSizingMode = SizingMode.Fill,
                    HeightSizingMode = SizingMode.AutoSize,
                    Parent = section,
                    ControlPadding = new Vector2(6, 0),
                };

                // Item icon
                if (!string.IsNullOrEmpty(item.IconUrl))
                {
                    new Image
                    {
                        Texture = GameService.Content.GetRenderServiceTexture(item.IconUrl),
                        Size = new Point(IconSize, IconSize),
                        Parent = rowPanel,
                    };
                }

                // Text column (name + detail stacked vertically)
                var textPanel = new FlowPanel
                {
                    FlowDirection = ControlFlowDirection.SingleTopToBottom,
                    WidthSizingMode = SizingMode.Fill,
                    HeightSizingMode = SizingMode.AutoSize,
                    Parent = rowPanel,
                };

                new Label
                {
                    Text = text,
                    TextColor = titleColor,
                    Font = GameService.Content.DefaultFont14,
                    AutoSizeHeight = true,
                    AutoSizeWidth = true,
                    Parent = textPanel
                };

                new Label
                {
                    Text = detail,
                    TextColor = Color.LightGray,
                    Font = GameService.Content.DefaultFont12,
                    AutoSizeHeight = true,
                    AutoSizeWidth = true,
                    Parent = textPanel
                };
            }
        }

        protected override void Unload()
        {
            _cornerIcon?.Dispose();
            _mainWindow?.Dispose();
        }
    }
}
