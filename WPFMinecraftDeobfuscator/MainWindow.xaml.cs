using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;

namespace MinecraftDeobfuscator {
    public partial class MainWindow : Window {
        private static readonly DependencyProperty MappingCountProperty = DependencyProperty.Register("MappingCount",
            typeof(int), typeof(MainWindow), new PropertyMetadata(0));
        private static readonly DependencyProperty FileCountProperty = DependencyProperty.Register("FileCount",
           typeof(int), typeof(MainWindow), new PropertyMetadata(0));
        private static readonly DependencyProperty JavaFileCountProperty = DependencyProperty.Register("JavaFileCount",
           typeof(int), typeof(MainWindow), new PropertyMetadata(0));
        private static readonly DependencyProperty MiscFileCountProperty = DependencyProperty.Register("MiscFileCount",
           typeof(int), typeof(MainWindow), new PropertyMetadata(0));

        private static readonly DependencyProperty OpeningFileProperty = DependencyProperty.Register("OpeningFile",
          typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
        private static readonly DependencyProperty MappingLoadedProperty = DependencyProperty.Register("MappingLoaded",
          typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        private static readonly DependencyProperty RunningProperty = DependencyProperty.Register("Running",
         typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        private static readonly DependencyPropertyKey ZipLoadedProperty = DependencyProperty.RegisterReadOnly("ZipLoaded",
          typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
        private static readonly DependencyPropertyKey InputFileProperty = DependencyProperty.RegisterReadOnly("InputFile",
          typeof(FileInfo), typeof(MainWindow), new PropertyMetadata(null));

        private OpenFileDialog OpenFileDialog { get; } = new OpenFileDialog() {
            Filter = "Zip and Jar Files|*.zip; *.jar",
            InitialDirectory = Environment.CurrentDirectory
        };

        private SaveFileDialog SaveFileDialog { get; } = new SaveFileDialog();

        private BinaryTree<char, string> MappingBinaryTree { get; set; } = new BinaryTree<char, string>();

        private MemoryStream OutStream { get; set; }
        private ZipArchive OutZip { get; set; }

        public int MappingCount {
            get => (int)GetValue(MappingCountProperty);
            set => SetValue(MappingCountProperty, value);
        }

        public int FileCount {
            get => (int)GetValue(FileCountProperty);
            set => SetValue(FileCountProperty, value);
        }
        public int JavaFileCount {
            get => (int)GetValue(JavaFileCountProperty);
            set => SetValue(JavaFileCountProperty, value);
        }
        public int MiscFileCount {
            get => (int)GetValue(MiscFileCountProperty);
            set => SetValue(MiscFileCountProperty, value);
        }
        public bool MappingLoaded {
            get => (bool)GetValue(MappingLoadedProperty);
            set => SetValue(MappingLoadedProperty, value);
        }
        public bool OpeningFile {
            get => (bool)GetValue(OpeningFileProperty);
            set => SetValue(OpeningFileProperty, value);
        }
        public bool Running {
            get => (bool)GetValue(RunningProperty);
            set {
                SetValue(RunningProperty, value);
                MCMappingPicker.IsDisabled = value;
            }
        }

        public bool FileDeobfuscated { get; private set; }

        public bool ZipLoaded {
            get => (bool)GetValue(ZipLoadedProperty.DependencyProperty);
            set => SetValue(ZipLoadedProperty, value);
        }
        public FileInfo InputFile {
            get => (FileInfo)GetValue(InputFileProperty.DependencyProperty);
            set => SetValue(InputFileProperty, value);
        }
        public FileInfo OutputFile { get; set; }

        public IEnumerable<ZipEntryInfo> ZipEntries { get; private set; }
        //public ParallelQuery<ZipEntryInfo> ZipEntries { get; private set; }

        public MainWindow() {
            InitializeComponent();
            DataContext = this;
            MCMappingPicker.OnCustomZipLoaded += UpdateMapping;
        }

        private void OnPopupClosed_Event(object sender, EventArgs e) {
            if (!MCMappingPicker.MappingChanged)
                return;
            if (MCMappingPicker.MappingChanged) {
                if (MCMappingPicker.Mapping != MCMapping.Custom)
                    UpdateMapping();
                MCMappingPicker.MappingChanged = false;
            }
        }

        public async void UpdateMapping() {
            MappingLoaded = false;
            MCMappingPicker.IsUpdating = true;
            MCMapping mapping = MCMappingPicker.Mapping;
            MappingCount = 0;
            MappingBinaryTree.Clear();

            if (mapping is null) {
                UpdateSearch();
                MCMappingPicker.IsUpdating = false;
                return;
            }

            if (mapping == MCMapping.SemiLive) {
                Log("Fetching Semi-Live Mappings");
            } else if (mapping == MCMapping.Custom) {
                Log("Loading " + mapping.ToString());
            } else {
                Log("Fetching " + mapping.ToString());
            }

            var task = new Task(() => {
                Partitioner.Create(mapping.GetMappingStreams()).AsParallel().ForAll(ParseStream);
            });
            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();
            task.Start();
            await task;
            timer.Stop();
            Log($"Took: {timer.ElapsedMilliseconds} ms");
            MappingCount = MappingBinaryTree.Count;
            System.Diagnostics.Debug.WriteLine("Count: " + MappingBinaryTree.Count);
            MappingLoaded = true;
            MCMappingPicker.IsUpdating = false;
            UpdateSearch();

            void ParseStream(Stream stream) {
                string line = null;
                string[] args = null;
                using (var sr = new StreamReader(stream)) {
                    try {
                        while ((line = sr.ReadLine()) != null) {
                            if (line.StartsWith("searge", StringComparison.OrdinalIgnoreCase) || line.StartsWith("param", StringComparison.OrdinalIgnoreCase))
                                continue;
                            args = line.Split(',');
                            if (args.Length < 2)
                                continue;
                            lock (MappingBinaryTree) {
                                if (args[0] == args[1])
                                    MappingBinaryTree.AddOrUpdate(args[0], null);
                                else
                                    MappingBinaryTree.AddOrUpdate(args[0], args[1]);
                            }
                        }
                    } catch (Exception ex) {
                        Log("Caught: " + ex);
                        Log("Line: " + line);
                        Log("Args: " + string.Join(",", args));
                    }
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) {
            var task = MCMappingPicker.UpdateMappingDataTask();
            MCMappingPicker.IsUpdating = true;
            task.Start();
            await task;
            if (MCMappingPicker.Mapping == MCMapping.SemiLive)
                UpdateMapping();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            OpeningFile = true;
            if (InputFile != null) {
                OpenFileDialog.InitialDirectory = InputFile.DirectoryName;
                OpenFileDialog.FileName = InputFile.Name;
            }

            if (OpenFileDialog.ShowDialog() ?? false) {
                JavaFileCount = 0;
                MiscFileCount = 0;

                ZipArchive zip = null;
                try {
                    ZipLoaded = false;
                    InputFile = null;

                    InputFile = new FileInfo(OpenFileDialog.FileName);
                    Log("Opening " + InputFile.Name);
                    MemoryStream zipMemory = new MemoryStream();
                    using (var fileStream = File.OpenRead(InputFile.FullName))
                        fileStream.CopyTo(zipMemory);

                    zip = new ZipArchive(zipMemory, ZipArchiveMode.Read);

                    ZipEntries = from entry in zip.Entries
                                 where !string.IsNullOrEmpty(entry.Name)
                                 select new ZipEntryInfo(entry) {
                                     Fullname = entry.FullName,
                                     IsJavaFile = entry.FullName.EndsWith(".java")
                                 };

                    int javaFiles = 0;
                    int misc = 0;
                    var timer = new System.Diagnostics.Stopwatch();
                    timer.Start();
                    Task task = Task.Run(() => {

                        ZipEntries = ZipEntries.ToArray();

                        foreach (var item in ZipEntries) {
                            if (item.IsJavaFile)
                                javaFiles++;
                            else
                                misc++;
                            Log(item.Fullname);
                        }
                    });
                    await task;
                    timer.Stop();
                    Log($"Finished Parsing file in {timer.ElapsedMilliseconds} ms");

                    JavaFileCount = javaFiles;
                    MiscFileCount = misc;
                    FileCount = JavaFileCount + MiscFileCount;
                    if (JavaFileCount <= 0)
                        throw new ArgumentException("No Java Files found. Did you forget to decompile?");

                    zip.Dispose();

                    ZipLoaded = true;
                    FileDeobfuscated = false;
                    Log("Finished opening " + InputFile.Name);
                } catch (Exception ex) {
                    Log("Error opening file: " + ex.Message);
                    zip?.Dispose();
                    MessageBox.Show(this, ex.Message, "A problem occured opening the file", MessageBoxButton.OK, MessageBoxImage.Error);
                    InputFile = null;
                    ZipLoaded = false;
                }
            }
            OpeningFile = false;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e) {
            if (Running)
                return;
            Running = true;
            Progress.Value = 0;
            Progress.Maximum = FileCount;

            if (OutputFile != null)
                SaveFileDialog.FileName = OutputFile.Name;
            SaveFileDialog.Filter = $"Output|*{InputFile.Extension}";

            if (SaveFileDialog.ShowDialog() ?? false) {
                OutputFile = new FileInfo(SaveFileDialog.FileName);

                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                var task = DeobfuscateZip();
                int count = await task;
                timer.Stop();
                Progress.Value = 0;
                Log($"Finished deobfuscating '{count}' entries in {timer.ElapsedMilliseconds} ms");
            }
            Running = false;
        }

        public Task<int> DeobfuscateZip() {
            return Task.Run(() => {
                int count = 0;

                using (var fileStream = File.Create(OutputFile.FullName))
                using (OutZip = new ZipArchive(OutStream = new MemoryStream(), ZipArchiveMode.Create, true)) {

                    ZipEntries.AsParallel().ForAll(item => {
                        if (item.IsJavaFile && !FileDeobfuscated) {
                            DeobfuscateSteam(item.data, out MemoryStream memory, ref count);
                            item.data = memory;
                        }

                        lock (OutZip)
                            using (var entry = OutZip.CreateEntry(item.Fullname).Open()) {
                                item.data.Position = 0;
                                item.data.CopyTo(entry);
                            }
                        Dispatcher.Invoke(() => Progress.Value++);
                    });

                    FileDeobfuscated = true;

                    OutStream.Position = 0;
                    OutStream.CopyTo(fileStream);
                }

                return count;
            });
        }

        private void DeobfuscateSteam(MemoryStream source, out MemoryStream output, ref int count) {
            source.Position = 0;
            output = new MemoryStream();
            long markPos = 0;
            BinaryTree<char, string>.Node currentNode = null;
            while (source.Position < source.Length) {
                char c = (char)source.ReadByte();
                if (currentNode is null) {
                    currentNode = MappingBinaryTree[c];
                    if (currentNode != null)
                        markPos = source.Position;
                } else {
                    currentNode = currentNode[c];
                    if (currentNode != null && currentNode.Value != null) {
                        output.Position = markPos;
                        output.Write(Encoding.ASCII.GetBytes(currentNode.Value), 0, currentNode.Value.Length);
                        count++;
                        currentNode = null;
                        continue;
                    }
                }
                output.WriteByte((byte)c);
            }
            source.Position = 0;
            output.Position = 0;
        }

        public void LogF(string msg, params object[] args) => Log(string.Format(msg, args));

        public void Log(string msg) {
            Dispatcher.BeginInvoke(new Action(() => Console.AppendText(msg + Environment.NewLine)));
            //Console.AppendText(msg + Environment.NewLine);
        }

        public void ClearLog(object sender = null, RoutedEventArgs e = null) {
            Console.Clear();
        }

        public void ExportLog(object sender = null, RoutedEventArgs e = null) {
        }

        public bool AutoScroll { get; set; } = true;
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e) {
            ScrollViewer viewer = (ScrollViewer)sender;
            if (e.ExtentHeightChange == 0) {
                if (viewer.VerticalOffset == viewer.ScrollableHeight)
                    AutoScroll = true;
                else
                    AutoScroll = false;
            } else if (AutoScroll)
                viewer.ScrollToBottom();
        }

        private void UpdateSearch(object sender = null, TextChangedEventArgs e = null) {
            ResultList.ItemsSource = null;
            if (MappingBinaryTree.Count <= 0)
                return;

            var searchText = SearchBar.Text.ToLower();
            var matchingKeys = from search in MappingBinaryTree.KeyValuePairs
                               where !string.IsNullOrEmpty(search.Value)
                               let key = ((string)search.Key).ToLower()
                               let val = search.Value.ToLower()
                               where key.Contains(searchText) || val.Contains(searchText)
                               select key.Contains(searchText) ? search.Value : (string)search.Key;
            ResultList.ItemsSource = matchingKeys;
        }

        private void CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
        }

        private void CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var selected = ResultList.SelectedItem;
            if (selected != null)
                Clipboard.SetText(selected.ToString());
        }

        private void ResultDBClicked(object sender, MouseButtonEventArgs e) {
            if (ResultList.Items.Count <= 0 || ResultList.SelectedItem is null)
                return;
            SearchBar.Text = ResultList.SelectedItem.ToString();
        }
    }

    public class ZipEntryInfo {
        public string Fullname;
        public bool IsJavaFile;
        public MemoryStream data;

        public ZipEntryInfo(ZipArchiveEntry entry) {
            using (var stream = entry.Open()) {
                data = new MemoryStream();
                stream.CopyTo(data);
                data.Position = 0;
            }
        }

    }
}
