﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CompilerCore;
using nwjsCookToolUI.Properties;
using Ookii.Dialogs.Wpf;

namespace nwjsCookToolUI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        #region Variables
        private static int _currentFile = 0;
        private static int _processorMode = 0;
        private static string[] _projectList;
        private static string _errorOutput;
        private readonly BackgroundWorker _compilerWorker = new BackgroundWorker();
        private readonly BackgroundWorker _mapCompilerWorker = new BackgroundWorker();
        #endregion Variables

        public MainWindow()
        {
            InitializeComponent();
            SetupWorkers();
        }

        #region Methods
        private void SetupWorkers()
        {
            _compilerWorker.WorkerReportsProgress = true;
            _compilerWorker.WorkerSupportsCancellation = true;
            _compilerWorker.DoWork += StartCompiler;
            _compilerWorker.ProgressChanged += CompilerReport;
            _compilerWorker.RunWorkerCompleted += CompilerFinisher;
            _mapCompilerWorker.WorkerReportsProgress = true;
            _mapCompilerWorker.WorkerSupportsCancellation = true;
            _mapCompilerWorker.DoWork += StartMapCompiler;
            _mapCompilerWorker.ProgressChanged += CompilerReport;

        }

        /// <summary>
        /// Locks or unlocks the settings.
        /// </summary>
        /// <param name="unlockSetting">Lock (false) or unlock (true).</param>
        private void UnlockSettings(bool unlockSetting)
        {
            RemoveCompiledJsCheckbox.IsEnabled = unlockSetting;
            PackageNwCheckbox.IsEnabled = unlockSetting;
            FileExtensionTextbox.IsEnabled = unlockSetting;
            BrowseSdkButton.IsEnabled = unlockSetting;
            RemoveFilesCheckBox.IsEnabled = unlockSetting && PackageNwCheckbox.IsChecked == true;
        }
        #endregion Methods

        #region Settings Code Set
        private void CookToolUi_Loaded(object sender, RoutedEventArgs e)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = fvi.FileVersion;
            ProgramVersionLabel.Content = ProgramVersionLabel.Content + @" (" + version + @")";
        }

        private void BrowseSDKButton_Click(object sender, RoutedEventArgs e)
        {
            var pickSdkFolder = new VistaFolderBrowserDialog
            {
                Description = Properties.Resources.SDKPickerText,
                UseDescriptionForTitle = true
            };
            var pickerResult = pickSdkFolder.ShowDialog();
            if (pickerResult != true) return;
            Settings.Default.SDKLocation = pickSdkFolder.SelectedPath;
            NwjsLocation.Text = pickSdkFolder.SelectedPath;
            Settings.Default.Save();
        }

        private void ReplacementCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var ci = CultureInfo.InstalledUICulture.ToString() == "el-GR" || CultureInfo.InstalledUICulture.ToString() == "el-CY" ? "el" : "en";
            var fileLocation = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), ci, "ReplacementCode.txt");
            if (File.Exists(fileLocation)) Process.Start(fileLocation);
            else
                MessageBox.Show(Properties.Resources.FileUnavailableText, Properties.Resources.InfoText, MessageBoxButton.OK,
                    MessageBoxImage.Information);
        }

        private void RemoveFilesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Default.Save();
        }

        private void FileExtensionTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.Default.Save();
        }
        #endregion Settings Code Set

        #region Quick Compile Code Set
        private void FindProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var pickProjectFolder =
                new VistaFolderBrowserDialog
                {
                    Description = Properties.Resources.ProjectPickerText,
                    UseDescriptionForTitle = true
                };
            var pickerResult = pickProjectFolder.ShowDialog();
            if (pickerResult != true) return;
            ProjectLocation.Text = pickProjectFolder.SelectedPath;
        }

        private void CompileButton_Click(object sender, RoutedEventArgs e)
        {
            CompileButton.IsEnabled = false;
            TestProjectButton.IsEnabled = false;
            ProjectLocation.IsEnabled = false;
            FindProjectButton.IsEnabled = false;
            UnlockSettings(false);
            MainProgress.Foreground = Brushes.ForestGreen;
            if (!File.Exists(Path.Combine(NwjsLocation.Text, "nwjc.exe")))
            {
                MessageBox.Show(Properties.Resources.CompilerMissingText, Properties.Resources.ErrorText, MessageBoxButton.OK, MessageBoxImage.Error);
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n"+ Properties.Resources.CompilerMissingText + "\n-----";
                CompileButton.IsEnabled = true;
                TestProjectButton.IsEnabled = true;
                ProjectLocation.IsEnabled = true;
                FindProjectButton.IsEnabled = true;
            }
            else if (!Directory.Exists(ProjectLocation.Text))
            {
                MessageBox.Show(Properties.Resources.NonExistantFolderText, Properties.Resources.ErrorText, MessageBoxButton.OK,
                    MessageBoxImage.Error);
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now +
                                  "\n"+ Properties.Resources.NonExistantFolderText +"\n-----";
                CompileButton.IsEnabled = true;
                TestProjectButton.IsEnabled = true;
                ProjectLocation.IsEnabled = true;
                FindProjectButton.IsEnabled = true;
                UnlockSettings(true);
            }
            else
            {
                Array.Resize(ref _projectList, 1);
                _projectList[0] = ProjectLocation.Text;
                MainProgress.Value = 0;
                MainProgress.Maximum = PackageNwCheckbox.IsChecked == true ? 4 : 3;
                _compilerWorker.RunWorkerAsync();
            }
        }

        private void StartCompiler(object sender, DoWorkEventArgs e)
        {
            var compilerInput = _projectList[0];
            try
            {
                var folderMap = "js";
                CoreCode.FileFinder(Path.Combine(compilerInput, "www", folderMap), "*.js");
                _compilerWorker.ReportProgress(_currentFile + 1);
                CoreCode.CleanupBin();
                CoreCode.CompilerInfo.FileName = Path.Combine(Settings.Default.SDKLocation, "nwjc.exe");
                Dispatcher.Invoke(() => MainProgress.Maximum = CoreCode.FileMap.Length);
                if (Settings.Default.PackageCode)
                    Dispatcher.Invoke(() =>  (Settings.Default.DeleteSourceCode) ? MainProgress.Maximum += 2 : 1);
                _processorMode = 1;
                for(_currentFile = 0; _currentFile < CoreCode.FileMap.Length; _currentFile++)
                {
                    if (_compilerWorker.CancellationPending) break;
                    CoreCode.CompilerWorkerTask(CoreCode.FileMap[_currentFile], Settings.Default.FileExtension, Settings.Default.DeleteSourceCode);
                    _compilerWorker.ReportProgress(_currentFile + 1);
                    _processorMode = 2;
                    Thread.Sleep(200);
                }

                if (!_compilerWorker.CancellationPending)
                {
                    if (Settings.Default.PackageCode)
                    {
                        _processorMode = 3;
                        _compilerWorker.ReportProgress(_currentFile + 1);
                        CoreCode.PreparePack(compilerInput);
                        _processorMode = 4;
                        _compilerWorker.ReportProgress(_currentFile + 2);
                        CoreCode.CompressFiles(compilerInput);
                        if (Settings.Default.DeleteSourceCode)
                        {
                            _processorMode = 5;
                            _compilerWorker.ReportProgress(_currentFile + 3);
                            CoreCode.DeleteFiles(compilerInput);
                        }
                    }
                }

                if (!_compilerWorker.CancellationPending)
                {
                    _processorMode = 6;
                    _compilerWorker.ReportProgress(_currentFile + 1);
                }
            }
            catch (ArgumentException exceptionOutput)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                    StatusLabel.Content = Properties.Resources.FailedText;
                });
                MessageBox.Show("An argument exception occured. Possibly a bug in the code.\nCheck the output in the About tab for more info.", Properties.Resources.FailedText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (FileNotFoundException exceptionOutput)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                    StatusLabel.Content = Properties.Resources.FailedText;
                });
                MessageBox.Show("A file was not found. Did a file got moved when the work was in progress?\nCheck the Output in the About tab for more info.", Properties.Resources.FailedText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (DirectoryNotFoundException exceptionOutput)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                    StatusLabel.Content = Properties.Resources.FailedText;
                });
                MessageBox.Show(
                    "A folder was not found. Did it got moved when the work was in progress?\nCheck the Output in the About tab for more info.",
                    Properties.Resources.FailedText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception exceptionOutput)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                    StatusLabel.Content = Properties.Resources.FailedText;
                });
               //errorOutput = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                MessageBox.Show(Properties.Resources.ErrorOccuredText, Properties.Resources.FailedText,
                    MessageBoxButton.OK, MessageBoxImage.Error);

            }

            Dispatcher.Invoke(() =>
            {
                CompileButton.IsEnabled = true;
                TestProjectButton.IsEnabled = true;
                ProjectLocation.IsEnabled = true;
                FindProjectButton.IsEnabled = true;
                UnlockSettings(true);
            });
        }

        private void CompilerReport(object sender, ProgressChangedEventArgs e)
        {
            switch (_processorMode)
            {
                case 6:
                    MainProgress.Value += 1;
                    break;
                case 5:
                    MainProgress.Value += 1;
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\nRemoving files...\n";
                    break;
                case 4:
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + Properties.Resources.PackageCreationText;
                    break;
                case 3:
                    StatusLabel.Content = Properties.Resources.PackaginStatusText;
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + Properties.Resources.FileCopyText;
                    break;
                case 2:
                    if (_currentFile < CoreCode.FileMap.Length)
                    {
                        MainProgress.Value = _currentFile;
                        OutputArea.Text += Properties.Resources.CompiledOutputText + DateTime.Now + "\n";
                        OutputArea.Text +=
                            "\n" + DateTime.Now + Properties.Resources.CompilingText + CoreCode.FileMap[_currentFile] +
                            "...\n";
                        StatusLabel.Content = StatusLabel.Content =
                            Properties.Resources.CompileText + CoreCode.FileMap[_currentFile] + "...";
                    }
                    break;
                case 1:
                    OutputArea.Text += "\n" + DateTime.Now + Properties.Resources.CompilingText + CoreCode.FileMap[_currentFile] +
                                       "...\n";
                    StatusLabel.Content = StatusLabel.Content = Properties.Resources.CompileText + CoreCode.FileMap[_currentFile] + "...";
                    break;
                case 0:
                    StatusLabel.Content = Properties.Resources.BinRemovalProgressText;
                    OutputArea.Text += "\n" + DateTime.Now +
                                       Properties.Resources.BinRemovalText;
                    break;
            }
        }

        private void CompilerFinisher(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + _errorOutput + "\n";
                StatusLabel.Content = Properties.Resources.FailedText;
            }
            else if (e.Cancelled)
            {
                OutputArea.Text += "\n" + DateTime.Now + "\n" + "The task was cancelled by the user." + "\n";
                MessageBox.Show("The task was cancelled.", "Aborted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(Properties.Resources.CompilationCompleteText, Properties.Resources.DoneText,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusLabel.Content = Properties.Resources.DoneText;
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n " + Properties.Resources.CompilationCompleteText + "\n";
            }
            MainProgress.Value = 0;
            MainProgress.Foreground = Brushes.ForestGreen;
            Array.Clear(CoreCode.FileMap, 0, CoreCode.FileMap.Length);

    }

        private void CancelTaskButton_Click(object sender, RoutedEventArgs e)
        {
            _compilerWorker.CancelAsync();
        }
        #endregion Quick Compile Code Set

        #region Batch Compile Code Set
        private void AddToMapButton_Click(object sender, RoutedEventArgs e)
        {
            var pickJsFolder =
                new VistaFolderBrowserDialog
                {
                    Description = Properties.Resources.ProjectPickerText,
                    UseDescriptionForTitle = true
                };
            var pickerResult = pickJsFolder.ShowDialog();
            if (pickerResult != true) return;
            if (pickJsFolder.SelectedPath != null) FolderList.Items.Add(pickJsFolder.SelectedPath);
        }

        private void RemoveFromMapButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in FolderList.SelectedItems.OfType<string>().ToList())
                FolderList.Items.Remove(s);
        }

        private void MapCompileButton_Click(object sender, RoutedEventArgs e)
        {
            MapCompileButton.IsEnabled = false;
            AddToMapButton.IsEnabled = false;
            RemoveFromMapButton.IsEnabled = false;
            MainProgress.Foreground = Brushes.ForestGreen;
            if (!File.Exists(Path.Combine(NwjsLocation.Text, "nwjc.exe")))
            {
                MessageBox.Show(Properties.Resources.CompilerMissingText, Properties.Resources.ErrorText, MessageBoxButton.OK, MessageBoxImage.Error);
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n"+ Properties.Resources.CompilerMissingText +  "\n-----";
                MapCompileButton.IsEnabled = true;
                AddToMapButton.IsEnabled = true;
                RemoveFromMapButton.IsEnabled = true;
            }
            else if (FolderList.Items.Count == 0)
            {
                MessageBox.Show(Properties.Resources.NoJSFilesPresent, Properties.Resources.ErrorText,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now +
                                  "\n"+ Properties.Resources.NonExistantFolderText + "\n-----";
                MapCompileButton.IsEnabled = true;
                AddToMapButton.IsEnabled = true;
                RemoveFromMapButton.IsEnabled = true;
            }
            else
            {
                
                //CoreCode.CompilerInfo.FileName = Dispatcher.Invoke(() => Path.Combine(NwjsLocation.Text, "nwjc.exe"));
                CoreCode.CompilerInfo.FileName = Path.Combine(Settings.Default.SDKLocation, "nwjc.exe");
                MapProgress.Value = 0;
                MapProgress.Maximum = FolderList.Items.Count;
                Array.Resize(ref _projectList, FolderList.Items.Count);
                _mapCompilerWorker.RunWorkerAsync();
            }
        }

        private void StartMapCompiler(object sender, DoWorkEventArgs e)
        {
            try
            {
                for (var i = 0; i < _projectList.Length; i++)
                {
                    var i1 = i;
                    Dispatcher.Invoke(() =>
                    {
                        OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + Properties.Resources.CompileText1 +
                                          FolderList.Items[i1] + Properties.Resources.FolderText + "\n";
                        MapStatusLabel.Content = Properties.Resources.CompileText1 + FolderList.Items[i1] +
                                                 Properties.Resources.FolderText;
                    });
                    CoreCode.FileFinder(FolderList.Items[i1].ToString(), "*.js");
                    Dispatcher.Invoke(() =>
                    {
                        OutputArea.Text += "\n" + DateTime.Now + Properties.Resources.BinRemovalText;
                        CurrentWorkloadLabel.Content =
                            Properties.Resources.BinRemovalStatusText + FolderList.Items[i1] + "...";
                    });
                    CoreCode.CleanupBin();
                    foreach (var file in CoreCode.FileMap)
                    {
                        Dispatcher.Invoke(() =>
                            CurrentWorkloadLabel.Content = Properties.Resources.CompileText + file + "..."
                        );
                        CoreCode.CompilerWorkerTask(file, Settings.Default.FileExtension,
                            Settings.Default.DeleteSourceCode);
                        Dispatcher.Invoke(() => CurrentWorkloadBar.Value += 1);

                    }
                    Array.Clear(CoreCode.FileMap, 0, CoreCode.FileMap.Length);
                    Dispatcher.Invoke(() =>
                    {
                        CurrentWorkloadBar.Value = 0;
                        MapProgress.Value += 1;
                    });
                }

                Dispatcher.Invoke(() =>
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + Properties.Resources.CompilationCompleteText + "\n");
                MessageBox.Show(Properties.Resources.CompilationCompleteText, Properties.Resources.DoneText, MessageBoxButton.OK, MessageBoxImage.Information);
                Dispatcher.Invoke(() =>
                {
                    MapStatusLabel.Content = Properties.Resources.DoneText;
                    CurrentWorkloadLabel.Content = Properties.Resources.DoneText;
                });
            }
            catch (Exception exceptionOutput)
            {
                Dispatcher.Invoke(() =>
                {
                    MapProgress.Foreground = Brushes.DarkRed;
                    OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + "\n" + exceptionOutput + "\n";
                    CurrentWorkloadBar.Foreground = Brushes.DarkRed;
                    CurrentWorkloadLabel.Content = Properties.Resources.FailedText;
                });
                MessageBox.Show(Properties.Resources.ErrorOccuredText, Properties.Resources.FailedText,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.Invoke(() =>
                {
                    CurrentWorkloadBar.Value = 0;
                    MapProgress.Value = 0;
                    MapProgress.Foreground = Brushes.ForestGreen;
                    CurrentWorkloadBar.Foreground = Brushes.ForestGreen;
                });
                Array.Clear(CoreCode.FileMap, 0, CoreCode.FileMap.Length);
                Array.Clear(_projectList, 0, _projectList.Length);
            }
            Dispatcher.Invoke(() =>
            {
                MapCompileButton.IsEnabled = true;
                AddToMapButton.IsEnabled = true;
                RemoveFromMapButton.IsEnabled = true;
                UnlockSettings(true);
            });
        }

        private void Checkbox_CheckChanged(object sender, RoutedEventArgs e)
        {
            Settings.Default.Save();
        }

        private void TestProjectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                CoreCode.RunTest(Settings.Default.SDKLocation, ProjectLocation.Text);
            }
            catch (Exception nwjsException)
            {
                MessageBox.Show(Properties.Resources.ErrorOccuredText, Properties.Resources.ErrorText, MessageBoxButton.OK, MessageBoxImage.Error);
                OutputArea.Text = OutputArea.Text + "\n" + DateTime.Now + nwjsException;
            }

        }
        #endregion Batch Compile Code Set

    }
}