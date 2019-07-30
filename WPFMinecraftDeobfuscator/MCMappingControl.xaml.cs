using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO.Compression;

namespace MinecraftDeobfuscator {
    public partial class MCMappingControl : UserControl {
        private static readonly DependencyProperty IsDisabledProperty = DependencyProperty.Register("IsDisabled",
            typeof(bool), typeof(MCMappingControl), new PropertyMetadata(false));

        private static readonly DependencyProperty PopUpOpenProperty = DependencyProperty.Register("PopUpOpen",
            typeof(bool), typeof(MCMappingControl), new PropertyMetadata(false));

        private static readonly DependencyProperty MappingProperty = DependencyProperty.Register("Mapping",
           typeof(MCMapping), typeof(MCMappingControl));

        private static readonly DependencyProperty MappingNameProperty = DependencyProperty.Register("MappingName",
           typeof(string), typeof(MCMappingControl), new PropertyMetadata("Select Mapping"));

        private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.Register("IsUpdating",
           typeof(bool), typeof(MCMappingControl), new PropertyMetadata(false));

        private DispatcherTimer RefreshTimer { get; }
        private SortedDictionary<string, Dictionary<MappingType, SortedSet<int>>> MappingData { get; set; } = new SortedDictionary<string, Dictionary<MappingType, SortedSet<int>>>();

        private OpenFileDialog OpenFileDialog { get; } = new OpenFileDialog() {
            Filter = "Zip|*.zip"
        };

        public event Action OnUpdate;
        public event Action OnUpdateFinished;
        public event Action<MCMapping> OnMappingChanged;
        public event Action OnCustomZipLoaded;

        public bool MappingChanged { get; set; }

        public bool IsDisabled {
            get {
                return (bool)GetValue(IsDisabledProperty);
            }
            set {
                SetValue(IsDisabledProperty, value);

                if (value) {
                    PopUpOpen = !value;
                }
            }
        }
        public bool PopUpOpen {
            get {
                return (bool)GetValue(PopUpOpenProperty);
            }
            set {
                SetValue(PopUpOpenProperty, value);
            }
        }
        public MCMapping Mapping {
            get {
                return (MCMapping)GetValue(MappingProperty);
            }
            set {
                if (value == Mapping)
                    return;
                SetValue(MappingProperty, value);
                if (TypeDropDown.SelectedIndex == -1 && TypeDropDown.IsEnabled)
                    TypeDropDown.SelectedIndex = 0;
                if (SnapshotDropDown.SelectedIndex == -1 && SnapshotDropDown.IsEnabled)
                    SnapshotDropDown.SelectedIndex = 0;
                if (value is null)
                    MappingName = "Select Mapping";
                else
                    MappingName = value.ToString();
                MappingChanged = true;
                OnMappingChanged?.Invoke(value);
            }
        }
        public string MappingName {
            get {
                return (string)GetValue(MappingNameProperty);
            }
            set {
                SetValue(MappingNameProperty, value);
            }
        }
        public bool IsUpdating {
            get {
                return (bool)GetValue(IsUpdatingProperty);
            }
            set {
                Dispatcher.Invoke(() => SetValue(IsUpdatingProperty, value));
            }
        }

        public event EventHandler OnPopupClosed;

        public MCMappingControl() {
            InitializeComponent();
            DataContext = this;
            TypeDropDown.ItemsSource = Enum.GetValues(typeof(MappingType));
            RefreshTimer = new DispatcherTimer() {
                // Updates list every 10 minutes
                Interval = TimeSpan.FromMinutes(10)
            };
            RefreshTimer.Tick += (sender, e) => {
                if (PopUp.IsOpen)
                    return;

                UpdateMappingData(sender, e);
            };
            PopUp.Closed += (s, e) => OnPopupClosed?.Invoke(s, e);
            UpdateMappingData();
        }

        private int OpenCustomMapping() {
            if (OpenFileDialog.ShowDialog() ?? false) {
                if (MCMapping.SetCustomZip(OpenFileDialog.FileName))
                    return 1;
                else
                    return -1;
            }
            return 0;
        }

        public Task UpdateMappingDataTask() {
            return new Task(() => {
                RefreshTimer.Stop();
                OnUpdate?.Invoke();
                JsonTextReader jReader;
                using (var client = new WebClient()) {
                    var data = Encoding.UTF8.GetString(client.DownloadData(Properties.Settings.Default.VersionJsonUrl));
                    jReader = new JsonTextReader(new StringReader(data));
                }

                MappingData.Clear();
                VersionJson jsonInput = VersionJson.Init;
                string keyVersion = null;
                MappingType keyMapType = MappingType.Snapshot;

                while (jReader.Read()) {
                    var value = jReader.Value;
                    if (jReader.Value != null) {
                        switch (jsonInput) {
                            case VersionJson.MCVersion:
                                MappingData[keyVersion = (string)value] = new Dictionary<MappingType, SortedSet<int>>();
                                break;
                            case VersionJson.MapType:
                                if (!Enum.TryParse((string)value, true, out keyMapType))
                                    throw new InvalidDataException("Expected Mapping type.");
                                break;
                            case VersionJson.Version:
                                if (!MappingData[keyVersion].ContainsKey(keyMapType))
                                    MappingData[keyVersion][keyMapType] = new SortedSet<int>();
                                MappingData[keyVersion][keyMapType].Add((int)(long)value);
                                break;
                            default:
                                break;
                        }
                    } else {
                        if (jReader.TokenType == JsonToken.StartObject)
                            jsonInput += 1;
                        else if (jReader.TokenType == JsonToken.StartArray)
                            jsonInput += 1;
                        else if (jReader.TokenType == JsonToken.EndObject)
                            jsonInput -= 1;
                        else if (jReader.TokenType == JsonToken.EndArray) {
                            jsonInput -= 1;
                        }
                    }
                }

                Dispatcher.Invoke(() => {
                    var versions = new List<string>(MappingData.Keys);
                    versions.Sort(MCVersionComparer.Comparer);
                    versions.Reverse();
                    versions.Insert(0, "Semi-Live");
                    versions.Add("Custom");
                    MCVersionDropDown.ItemsSource = versions;
                });

                RefreshTimer.Start();
                OnUpdateFinished?.Invoke();
                IsUpdating = false;
            });
        }

        public void UpdateMappingData(object sender = null, EventArgs e = null) {
            IsUpdating = true;
            UpdateMappingDataTask().Start();
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            PopUp.IsOpen = !IsDisabled && !PopUp.IsOpen;
        }

        private void MCVersionDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (MCVersionDropDown.Items.Count <= 0)
                Mapping = null;
            else if (MCVersionDropDown.SelectedIndex == 0)
                Mapping = MCMapping.SemiLive;
            else if (MCVersionDropDown.SelectedIndex == MCVersionDropDown.Items.Count - 1) {
                Mapping = null;
                int result = OpenCustomMapping();
                if (result != 1) {
                    MCVersionDropDown.SelectedIndex = -1;
                    if (result == -1)
                        MappingName = "Invalid Custom";
                } else {
                    Mapping = MCMapping.Custom;
                    MappingName = OpenFileDialog.SafeFileName;
                    OnCustomZipLoaded?.Invoke();
                }
            } else {
                Mapping = null;
                if (TypeDropDown.SelectedIndex < 0)
                    TypeDropDown.SelectedIndex = 0;
                else {
                    try {
                        SnapshotDropDown.ItemsSource = MappingData[(string)MCVersionDropDown.SelectedValue][(MappingType)TypeDropDown.SelectedValue].Reverse();
                        SnapshotDropDown.SelectedIndex = 0;
                        Mapping = new MCMapping((string)MCVersionDropDown.SelectedValue, (MappingType)TypeDropDown.SelectedValue, (int)SnapshotDropDown.SelectedValue);
                    } catch (Exception) {
                        SnapshotDropDown.ItemsSource = null;
                    }
                }
            }
        }

        private void TypeDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (Mapping == MCMapping.SemiLive || Mapping == MCMapping.Custom) {
                SnapshotDropDown.ItemsSource = null;
                return;
            }
            if (TypeDropDown.Items.Count > 0 && TypeDropDown.SelectedIndex >= 0)
                try {
                    SnapshotDropDown.ItemsSource = MappingData[(string)MCVersionDropDown.SelectedValue][(MappingType)TypeDropDown.SelectedValue].Reverse();
                    SnapshotDropDown.SelectedIndex = 0;
                    Mapping = new MCMapping((string)MCVersionDropDown.SelectedValue, (MappingType)TypeDropDown.SelectedValue, (int)SnapshotDropDown.SelectedValue);
                } catch (Exception) {
                    SnapshotDropDown.ItemsSource = null;
                }
            else
                SnapshotDropDown.ItemsSource = null;
        }

        private void SnapshotDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (SnapshotDropDown.Items.Count <= 0)
                Mapping = null;
            else
                try {
                    Mapping = new MCMapping((string)MCVersionDropDown.SelectedValue, (MappingType)TypeDropDown.SelectedValue, (int)SnapshotDropDown.SelectedValue);
                } catch (Exception) {
                    Mapping = null;
                }
        }
    }

    public enum MappingType {
        Snapshot, Stable
    }

    public class MCMapping {
        public static readonly MCMapping SemiLive = new MCMapping();
        public static readonly MCMapping Custom = new MCMapping();

        private static string CustomZipFilePath { get; set; }

        public string MCVersion { get; private set; }
        public MappingType Type { get; private set; }
        public int Snapshot { get; private set; }

        public static readonly DependencyPropertyKey MappingProperty = DependencyProperty.RegisterReadOnly("SemiLive", typeof(MCMapping),
            typeof(MCMappingControl), new PropertyMetadata(SemiLive));

        private MCMapping() { }

        public MCMapping(string mcVersion, MappingType mappingType, int snapshot) {
            MCVersion = mcVersion;
            Type = mappingType;
            Snapshot = snapshot;
        }

        public override string ToString() {
            if (this == SemiLive)
                return "Semi-Live";
            if (this == Custom)
                return "Custom";
            if (Type == MappingType.Stable)
                return $"{MCVersion}-{Type}-{Snapshot}";
            return $"{MCVersion}-{Snapshot}";
        }

        public static bool SetCustomZip(string filePath) {
            try {
                var zip = ZipFile.OpenRead(filePath);
                zip.GetEntry("fields.csv");
                zip.GetEntry("params.csv");
                zip.GetEntry("params.csv");
                zip.Dispose();
                CustomZipFilePath = filePath;
                return true;
            } catch (Exception) {
                return false;
            }
        }

        public static IEnumerable<MemoryStream> GetLiveMappingStreams() {
            MemoryStream[] streams = new MemoryStream[3];
            var task1 = DownloadData(Properties.Settings.Default.LiveFields);
            task1.Start();
            var task2 = DownloadData(Properties.Settings.Default.LiveMethods);
            task2.Start();
            var task3 = DownloadData(Properties.Settings.Default.LiveParams);
            task3.Start();

            streams[0] = task1.Result;
            streams[1] = task2.Result;
            streams[2] = task3.Result;
            return streams;

            Task<MemoryStream> DownloadData(string url) {
                return new Task<MemoryStream>(() => {
                    using (var client = new WebClient()) {
                        byte[] data = client.DownloadData(url);
                        MemoryStream memory = new MemoryStream(data);
                        return memory;
                    }
                });
            }
        }

        private ZipArchive customZip;

        public IEnumerable<Stream> GetMappingStreams() {
            if (this == SemiLive)
                return GetLiveMappingStreams();
            else if (this == Custom) {
                if (CustomZipFilePath != null) {
                    customZip?.Dispose();
                    MemoryStream data = new MemoryStream();
                    var file = File.OpenRead(CustomZipFilePath);
                    file.CopyTo(data);
                    file.Close();
                    return ParseZip(customZip = new ZipArchive(data));
                }
            } else {
                using (var client = new WebClient()) {
                    byte[] data = null;
                    if (Type == MappingType.Snapshot)
                        data = client.DownloadData(string.Format(Properties.Settings.Default.SnapshotZipUrl, Snapshot, MCVersion));
                    else if (Type == MappingType.Stable)
                        data = client.DownloadData(string.Format(Properties.Settings.Default.StableZipUrl, Snapshot, MCVersion));
                    if (data is null)
                        return null;
                    return ParseZip(new ZipArchive(new MemoryStream(data)));
                }
            }
            return null;

            IEnumerable<Stream> ParseZip(ZipArchive zip) {
                List<Stream> streams = new List<Stream>();
                foreach (var item in zip.Entries) {
                    streams.Add(item.Open());
                }
                return streams;
            }
        }
    }

    public enum VersionJson {
        Init, MCVersion, MapType, Version
    }

    public class MCVersionComparer : IComparer<string> {
        public static MCVersionComparer Comparer { get; } = new MCVersionComparer();

        public int Compare(string x, string y) {
            int[] versions1 = x.Split('.').Select(int.Parse).ToArray();
            int[] versions2 = y.Split('.').Select(int.Parse).ToArray();
            try {
                if (versions1[0] != versions2[0])
                    return versions1[0] > versions2[0] ? 1 : -1;
                else if (versions1[1] != versions2[1])
                    return versions1[1] > versions2[1] ? 1 : -1;
                else if (versions1[2] != versions2[2])
                    return versions1[2] > versions2[2] ? 1 : -1;
            } catch (IndexOutOfRangeException) {
                return versions1.Length > versions2.Length ? 1 : -1;
            }
            return 0;
        }
    }
}
