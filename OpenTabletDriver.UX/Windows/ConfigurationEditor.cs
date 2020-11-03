﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using HidSharp;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Tablet;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Windows
{
    public class ConfigurationEditor : Form
    {
        public ConfigurationEditor()
        {
            base.Title = "Configuration Editor";
            base.ClientSize = new Size(960 - 50, 730 - 50);
            base.MinimumSize = new Size(960 - 50, 730 - 50);
            base.Icon = App.Logo.WithSize(App.Logo.Size);

            // Main Controls
            _configList.SelectedIndexChanged += (sender, e) => 
            {
                if (_configList.SelectedIndex >= 0)
                    SelectedConfiguration = Configurations[_configList.SelectedIndex];
            };
            
            base.Content = new Splitter
            {
                Orientation = Orientation.Horizontal,
                Panel1MinimumSize = 200,
                Panel1 = _configList,
                Panel2 = new Scrollable
                { 
                    Content = _configControls,
                    Padding = new Padding(5)
                },
            };

            // MenuBar Commands
            var quitCommand = new Command { MenuText = "Close", Shortcut = Application.Instance.CommonModifier | Keys.W };
            quitCommand.Executed += (sender, e) => Close();

            var loadDirectory = new Command { MenuText = "Load configurations...", Shortcut = Application.Instance.CommonModifier | Keys.O };
            loadDirectory.Executed += (sender, e) => LoadConfigurationsDialog();

            var saveDirectory = new Command { MenuText = "Save configurations", Shortcut = Application.Instance.CommonModifier | Keys.S };
            saveDirectory.Executed += (sender, e) => WriteConfigurations(Configurations, new DirectoryInfo(AppInfo.Current.ConfigurationDirectory));

            var saveToDirectory = new Command { MenuText = "Save configurations to...", Shortcut = Application.Instance.CommonModifier | Application.Instance.AlternateModifier | Keys.S };
            saveToDirectory.Executed += (sender, e) => SaveConfigurationsDialog();

            var newConfiguration = new Command { ToolBarText = "New configuration", Shortcut = Application.Instance.CommonModifier | Keys.N };
            newConfiguration.Executed += (sender, e) => CreateNewConfiguration();

            var deleteConfiguration = new Command { ToolBarText = "Delete configuration" };
            deleteConfiguration.Executed += (sender, e) => DeleteConfiguration(SelectedConfiguration);

            var generateConfiguration = new Command { ToolBarText = "Generate configuration..." };
            generateConfiguration.Executed += async (sender, e) => await GenerateConfiguration();

            // Menu
            base.Menu = new MenuBar
            {
                Items =
                {
                    // File submenu
                    new ButtonMenuItem
                    {
                        Text = "&File",
                        Items =
                        {
                            loadDirectory,
                            saveDirectory,
                            saveToDirectory
                        }
                    }
                },
                QuitItem = quitCommand
            };

            base.ToolBar = new ToolBar
            {
                Items =
                {
                    newConfiguration,
                    deleteConfiguration,
                    generateConfiguration
                }
            };

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            var appinfo = await App.Driver.Instance.GetApplicationInfo();
            var configDir = new DirectoryInfo(appinfo.ConfigurationDirectory);
            var sortedConfigs = from config in ReadConfigurations(configDir)
                orderby config.Name
                select config;
            Configurations = new List<TabletConfiguration>(sortedConfigs);
            _configList.SelectedIndex = 0;
        }

        private List<TabletConfiguration> _configs;
        private List<TabletConfiguration> Configurations
        {
            set
            {
                _configs = value;
                _configList.Items.Clear();
                foreach (var config in Configurations)
                    _configList.Items.Add(config.Name);
            }
            get => _configs;
        }
        
        private TabletConfiguration _selected;
        private TabletConfiguration SelectedConfiguration
        {
            set
            {
                _selected = value;
                Refresh();
            }
            get => _selected;
        }

        private ListBox _configList = new ListBox();
        private StackView _configControls = new StackView();

        private List<TabletConfiguration> ReadConfigurations(DirectoryInfo dir)
        {
            var configs = from file in dir.GetFiles("*.json", SearchOption.AllDirectories)
                select TabletConfiguration.Read(file);
            return new List<TabletConfiguration>(configs);
        }

        private void WriteConfigurations(IEnumerable<TabletConfiguration> configs, DirectoryInfo dir)
        {
            var regex = new Regex("(?<Manufacturer>.+?) (?<TabletName>.+?)$");
            foreach (var config in configs)
            {
                var match = regex.Match(config.Name);
                var manufacturer = match.Groups["Manufacturer"].Value;
                var tabletName = match.Groups["TabletName"].Value;

                var path = Path.Join(dir.FullName, manufacturer, string.Format("{0}.json", tabletName));
                var file = new FileInfo(path);
                if (!file.Directory.Exists)
                    file.Directory.Create();
                config.Write(file);
            }
        }

        private void LoadConfigurationsDialog()
        {
            var folderDialog = new SelectFolderDialog
            {
                Title = "Open configuration folder..."
            };
            switch (folderDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var dir = new DirectoryInfo(folderDialog.Directory);
                    Configurations = ReadConfigurations(dir);
                    break;
            }
        }

        private void SaveConfigurationsDialog()
        {
            var folderDialog = new SelectFolderDialog
            {
                Title = "Save configurations to..."
            };
            switch (folderDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var dir = new DirectoryInfo(folderDialog.Directory);
                    WriteConfigurations(Configurations, dir);
                    break;
            }
        }

        private void CreateNewConfiguration()
        {
            var newTablet = new TabletConfiguration
            {
                Name = "New Tablet"
            };
            Configurations = Configurations.Append(newTablet).ToList();
            _configList.SelectedIndex = Configurations.IndexOf(newTablet);
        }

        private void DeleteConfiguration(TabletConfiguration config)
        {
            Configurations = Configurations.Where(c => c != config).ToList();
            if (SelectedConfiguration == config)
                _configList.SelectedIndex = Configurations.Count - 1;
        }

        private async Task GenerateConfiguration()
        {
            var dialog = new DeviceListDialog();
            if (await dialog.ShowModalAsync() is HidDevice device)
            {
                try
                {
                    var generatedConfig = new TabletConfiguration
                    {
                        Name = device.GetManufacturer() + " " + device.GetProductName(),
                        DigitizerIdentifiers =
                        {
                            new DigitizerIdentifier
                            {
                                VendorID = device.VendorID,
                                ProductID = device.ProductID,
                                InputReportLength = (uint)device.GetMaxInputReportLength(),
                                OutputReportLength = (uint)device.GetMaxOutputReportLength()
                            }
                        }
                    };
                    Configurations = Configurations.Append(generatedConfig).ToList();
                    _configList.SelectedIndex = Configurations.IndexOf(generatedConfig);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            }
        }

        private void Refresh()
        {
            _configControls.Items.Clear();
            _configControls.AddControls(MakePropertyControls());
        }

        private IEnumerable<Control> MakePropertyControls()
        {
            yield return new InputBox("Name",
                () => SelectedConfiguration.Name,
                (o) => SelectedConfiguration.Name = o
            );

            var digitizerStack = new StackView();
            digitizerStack.AddControl(
                new Button(
                    (_, e) =>
                    {
                        SelectedConfiguration.DigitizerIdentifiers.Add(new DigitizerIdentifier());
                        Refresh();
                    }
                )
                {
                    Text = "Add"
                }
            );

            foreach (var digitizerIdentifier in SelectedConfiguration.DigitizerIdentifiers)
            {
                var expander = new ExpanderBase("Digitizer Identifier", isExpanded: false);
                expander.StackView.AddControls(MakeDigitizerIdentifierControls(digitizerIdentifier));
                expander.StackView.AddControl(
                    new Button(
                        (_, e) => 
                        {
                            SelectedConfiguration.DigitizerIdentifiers.Remove(digitizerIdentifier);
                            Refresh();
                        }
                    )
                    {
                        Text = "Remove"
                    }
                );

                digitizerStack.AddControl(new GroupBoxBase(null, expander));
            }
            yield return new GroupBoxBase("Digitizer Identifiers", digitizerStack);

            var auxStack = new StackView();
            auxStack.AddControl(
                new Button(
                    (_, e) =>
                    {
                        SelectedConfiguration.AuxilaryDeviceIdentifiers.Add(new DeviceIdentifier());
                        Refresh();
                    }
                )
                {
                    Text = "Add"
                }
            );

            foreach (var auxIdentifier in SelectedConfiguration.AuxilaryDeviceIdentifiers)
            {
                var expander = new ExpanderBase("Auxiliary Identifier", isExpanded: false);
                expander.StackView.AddControls(GetDeviceIdentifierControls(auxIdentifier));
                expander.StackView.AddControl(
                    new Button(
                        (_, e) => 
                        {
                            SelectedConfiguration.AuxilaryDeviceIdentifiers.Remove(auxIdentifier);
                            Refresh();
                        }
                    )
                    {
                        Text = "Remove"
                    }
                );
                
                auxStack.AddControl(new GroupBoxBase(null, expander));
            }
            yield return new GroupBoxBase("Auxiliary Device Identifiers", auxStack);

            yield return new DictionaryEditor("Attributes",
                () => SelectedConfiguration.Attributes,
                (o) => SelectedConfiguration.Attributes = o
            );
        }

        private IEnumerable<Control> GetDeviceIdentifierControls(DeviceIdentifier id)
        {
            yield return new InputBox("Vendor ID",
                () => id.VendorID.ToString(),
                (o) => id.VendorID = ToInt(o)
            );
            yield return new InputBox("Product ID",
                () => id.ProductID.ToString(),
                (o) => id.ProductID = ToInt(o)
            );
            yield return new InputBox("Input Report Length",
                () => id.InputReportLength.ToString(),
                (o) => id.InputReportLength = ToNullableUInt(o)
            );
            yield return new InputBox("Output Report Length",
                () => id.OutputReportLength.ToString(),
                (o) => id.OutputReportLength = ToNullableUInt(o)
            );
            yield return new InputBox("Report Parser",
                () => id.ReportParser,
                (o) => id.ReportParser = o,
                id is DigitizerIdentifier ? typeof(TabletReportParser).FullName : typeof(AuxReportParser).FullName
            );
            yield return new InputBox("Feature Initialization Report",
                () => ToHexString(id.FeatureInitReport),
                (o) => id.FeatureInitReport = ToByteArray(o)
            );
            yield return new InputBox("Output Initialization Report",
                () => ToHexString(id.OutputInitReport),
                (o) => id.OutputInitReport = ToByteArray(o)
            );
            yield return new DictionaryEditor("Device Strings",
                () =>
                {
                    var dictionaryBuffer = new Dictionary<string, string>();
                    foreach (var pair in id.DeviceStrings)
                        dictionaryBuffer.Add($"{pair.Key}", pair.Value);
                    return dictionaryBuffer;
                },
                (o) =>
                {
                    id.DeviceStrings.Clear();
                    foreach (KeyValuePair<string, string> pair in o)
                        if (byte.TryParse(pair.Key, out var keyByte))
                            id.DeviceStrings.Add(keyByte, pair.Value);
                }
            );
            yield return new ListEditor("Initialization String Indexes",
                () =>
                {
                    var listBuffer = new List<string>();
                    foreach (var value in id.InitializationStrings)
                        listBuffer.Add($"{value}");
                    return listBuffer;
                },
                (o) =>
                {
                    id.InitializationStrings.Clear();
                    foreach (string value in o)
                        if (byte.TryParse(value, out var byteValue))
                            id.InitializationStrings.Add(byteValue);
                }
            );
        }

        private IEnumerable<Control> MakeDigitizerIdentifierControls(DigitizerIdentifier id)
        {
            yield return new InputBox("Width (mm)",
                () => id.Width.ToString(),
                (o) => id.Width = ToFloat(o)
            );
            yield return new InputBox("Height (mm)",
                () => id.Height.ToString(),
                (o) => id.Height = ToFloat(o)
            );
            yield return new InputBox("Max X (px)",
                () => id.MaxX.ToString(),
                (o) => id.MaxX = ToFloat(o)
            );
            yield return new InputBox("Max Y (px)",
                () => id.MaxY.ToString(),
                (o) => id.MaxY = ToFloat(o)
            );
            yield return new InputBox("Max Pressure",
                () => id.MaxPressure.ToString(),
                (o) => id.MaxPressure = ToUInt(o)
            );
            yield return new InputBox("Active Report ID",
                () => id.ActiveReportID?.ToString() ?? new DetectionRange().ToString(),
                (o) => id.ActiveReportID = DetectionRange.Parse(o)
            );
            foreach (var control in GetDeviceIdentifierControls(id))
                yield return control;
        }

        private static float? ToNullableFloat(string str) => float.TryParse(str, out var val) ? val : (float?)null;
        private static float ToFloat(string str) => ToNullableFloat(str) ?? 0f;
        
        private static int? ToNullableInt(string str) => int.TryParse(str, out var val) ? val : (int?)null;
        private static int ToInt(string str) => ToNullableInt(str) ?? 0;
                
        private static uint? ToNullableUInt(string str) => uint.TryParse(str, out var val) ? val : (uint?)null;
        private static uint ToUInt(string str) => ToNullableUInt(str) ?? 0;

        private static bool TryGetHexValue(string str, out byte value) => byte.TryParse(str.Replace("0x", string.Empty), NumberStyles.HexNumber, null, out value);
        
        private static string ToHexString(byte[] value)
        {
            if (value is byte[] array)
                return "0x" + BitConverter.ToString(array).Replace("-", " 0x") ?? string.Empty;
            else
                return string.Empty;
        }
        
        private static byte[] ToByteArray(string hex)
        {
            var raw = hex.Split(' ');
            byte[] buffer = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (TryGetHexValue(raw[i], out var val))
                    buffer[i] = val;
                else
                    return null;
            }
            return buffer;
        }
    }
}