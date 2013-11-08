using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Practices.Prism.Commands;
using OCRLessonReport.Helpers;
using OCRLessonReport.Imaging;
using OCRLessonReport.Models;
using OCRLessonReport.Properties;

namespace OCRLessonReport.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private readonly ISettingsManager settingsManager;

        private ICommand openFileCommand;
        private ICommand openWebCamCommand;
        private ICommand shotCommand;
        private ICommand closeWebCamCommand;
        private ICommand recognizeCommand;
        private ICommand saveCommand;

        private VideoCaptureDevice videoSource;

        public MainWindowViewModel()
        {
            settingsManager = new SettingsManager();

            //Configure tesseract engine for local path
            System.Environment.SetEnvironmentVariable("TESSDATA_PREFIX", settingsManager.Settings.TessdataPrefix);

            //var imageprocessor = new ImageProcessor(Resources.DSC02127, settingsManager);
            //imageprocessor.ProccessImage(null);
            //SourceImage = imageprocessor.SourceBitmapImage;
            //cells = imageprocessor.Cells.ToList();
            InitCommands();
            InitWorker();
        }

        private void InitCommands()
        {
            openFileCommand = new DelegateCommand(OpenFile, CanOpenFile);
            openWebCamCommand = new DelegateCommand(OpenWebCam, CanOpenWebCam);
            shotCommand = new DelegateCommand(Shot);
            closeWebCamCommand = new DelegateCommand(CloseWebCam);
            recognizeCommand = new DelegateCommand(Recognize, CanRecognize);
            saveCommand = new DelegateCommand(Save, CanSave);
        }

        #region Properties

        #region Commands

        public ICommand OpenFileCommand
        {
            get
            {
                return openFileCommand;
            }
        }

        public ICommand OpenWebCamCommand
        {
            get
            {
                return openWebCamCommand;
            }
        }

        public ICommand ShotCommand
        {
            get
            {
                return shotCommand;
            }
        }

        public ICommand CloseWebCamCommand
        {
            get
            {
                return closeWebCamCommand;
            }
        }


        public ICommand RecognizeCommand
        {
            get
            {
                return recognizeCommand;
            }
        }

        public ICommand SaveCommand
        {
            get
            {
                return saveCommand;
            }
        }

        #endregion

        private ImageDataSource imageDataSource = ImageDataSource.File;

        public ImageDataSource ImageDataSource
        {
            get
            {
                return imageDataSource;
            }
            set
            {
                if (imageDataSource == value) return;
                imageDataSource = value;

                if (imageDataSource == ViewModels.ImageDataSource.Lib)
                    SourceImage = Resources.InternalImage.ToBitmapImage();

                RaisePropertyChanged(() => ImageDataSource);
                RaiseCommandsExecute();
            }
        }

        private string filePath;

        public string FilePath
        {
            get
            {
                return filePath;
            }
            set
            {
                if (filePath == value) return;
                filePath = value;
                RaisePropertyChanged(() => FilePath);
            }
        }

        public bool IsFileSourceEnabled
        {
            get
            {
                return imageDataSource == ImageDataSource.File;
            }
        }

        public bool IsWebCamEnabled
        {
            get
            {
                return imageDataSource == ImageDataSource.Webcam;
            }
        }

        private bool isWebCamOpened;

        public bool IsWebCamOpened
        {
            get
            {
                return isWebCamOpened;
            }
            set
            {
                if (isWebCamOpened == value) return;
                isWebCamOpened = value;
                RaisePropertyChanged(() => IsWebCamOpened);
            }
        }

        private BitmapImage sourceImage;

        public BitmapImage SourceImage
        {
            get
            {
                return sourceImage;
            }
            set
            {
                if (sourceImage == value) return;
                sourceImage = value;
                RaisePropertyChanged(() => sourceImage);
                RaiseCommandsExecute();
            }
        }

        private BitmapImage webCamImage;

        public BitmapImage WebCamImage
        {
            get
            {
                return webCamImage;
            }
            set
            {
                if (webCamImage == value) return;
                webCamImage = value;
                RaisePropertyChanged(() => WebCamImage);
            }
        }

        public List<ColumnItem> Columns
        {
            get;
            set;
        }

        public DataView DataView
        {
            get;
            set;
        }

        private List<TableCell> cells;

        public List<TableCell> Cells
        {
            get
            {
                return cells;
            }
        }

        public bool IsBusy
        {
            get
            {
                return worker != null && worker.IsBusy;
            }
        }

        private int progress;

        public int Progress
        {
            get
            {
                return progress;
            }
            set
            {
                if (progress == value) return;
                progress = value;
                RaisePropertyChanged(() => Progress);
            }
        }

        private ProcessingStages status = ProcessingStages.Ready;

        public ProcessingStages Status
        {
            get
            {
                return status;
            }
            set
            {
                if (status == value) return;
                status = value;
                RaisePropertyChanged(() => Status);
            }
        }

        #endregion

        #region Infrastructure

        public void Close()
        {
            if (videoSource != null)
                videoSource.Stop();
        }

        private void OpenFile()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Image Files (JPG, PNG, TIFF)|*.JPG;*.PNG;*.TIFF";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                FilePath = ofd.FileName;

                try
                {
                    var bitmap = new Bitmap(FilePath);
                    SourceImage = bitmap.ToBitmapImage();
                }
                catch
                {
                }
            }
        }

        private bool CanOpenFile()
        {
            return imageDataSource == ImageDataSource.File && !IsBusy;
        }

        private void OpenWebCam()
        {
            if (videoSource == null)
            {
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);

                videoSource.NewFrame += (o, e) =>
                {
                    var capturedImage = ((Bitmap)e.Frame.Clone()).ToBitmapImage();
                    capturedImage.Freeze();
                    WebCamImage = capturedImage;
                };

                videoSource.PlayingFinished += (o, r) =>
                {
                    WebCamImage = null;
                    RaiseCommandsExecute();
                };
            }

            IsWebCamOpened = true;

            videoSource.Start();

            RaiseCommandsExecute();
        }

        private bool CanOpenWebCam()
        {
            return imageDataSource == ImageDataSource.Webcam && (videoSource != null && !videoSource.IsRunning || videoSource == null) && !IsBusy;
        }

        private void CloseWebCam()
        {
            if (videoSource != null)
                videoSource.SignalToStop();            

            IsWebCamOpened = false;
            RaiseCommandsExecute();
        }

        private void Shot()
        {
            if (videoSource != null)
                SourceImage = WebCamImage;

        }

        private BackgroundWorker worker = new BackgroundWorker();

        private void InitWorker()
        {
            worker.WorkerReportsProgress = true;

            worker.DoWork += (o, e) =>
            {
                Status = ProcessingStages.Processing;
                var imageProcessor = new ImageProcessor(SourceImage.ToBitmap(), settingsManager);
                imageProcessor.ProccessImage(worker);
                e.Result = imageProcessor;
            };

            worker.ProgressChanged += (o, e) =>
            {
                Progress = e.ProgressPercentage;
            };

            worker.RunWorkerCompleted += (o, e) =>
            {
                if (e.Error != null)
                {
                    ShowError(e.Error.Message);
                    Status = ProcessingStages.Error;
                }
                else
                {
                    Status = ProcessingStages.Completed;
                    Progress = 0;
                    var imageProcessor = e.Result as ImageProcessor;
                    cells = imageProcessor.Cells;
                    RaisePropertyChanged(() => Cells);
                    RaisePropertyChanged(() => IsBusy);
                    RaiseCommandsExecute();
                    
                }
            };
        }

        private void Recognize()
        {
            if (worker != null && !worker.IsBusy)
                worker.RunWorkerAsync();

            RaisePropertyChanged(() => IsBusy);
            RaiseCommandsExecute();
        }

        private bool CanRecognize()
        {
            return SourceImage != null && !IsBusy;
        }

        private void Save()
        {          
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Access DB file (*.mdb)|*.mdb";

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                Repository rep = new Repository(settingsManager);
                
                try
                {
                    rep.CreateAndExportLegacyFile(sfd.FileName, cells);
                    Status = ProcessingStages.Ready;
                }
                catch
                {
                    ShowError();
                }
            }
        }

        private bool CanSave()
        {
            return Cells != null && Cells.Count > 0;
        }

        private void ShowError(string message = "")
        {
            if (String.IsNullOrEmpty(message))
                message = "Unexpected error.";
            
            System.Windows.MessageBox.Show(String.Format("Error occured: {0}", message));
        }

        private void RaiseCommandsExecute()
        {
            (OpenFileCommand as DelegateCommand).RaiseCanExecuteChanged();
            (OpenWebCamCommand as DelegateCommand).RaiseCanExecuteChanged();
            (RecognizeCommand as DelegateCommand).RaiseCanExecuteChanged();
            (SaveCommand as DelegateCommand).RaiseCanExecuteChanged();
        }

        [Obsolete]
        private void BuildResultTable()
        {
            DataTable dt = new DataTable();

            var rows = Cells.GroupBy(c => c.Row).Select(p => new { Row = p.Key, Cells = p.ToArray() }).ToArray();

            if (rows.Length > 0)
            {
                Columns = rows[0].Cells.Select(c => new ColumnItem(c.Text.Trim(), new DataColumn())).ToList();

                dt.Columns.AddRange(Columns.Select(c => c.DataColumn).ToArray());

                for (int row = 1; row < rows.Length; row++)
                {
                    DataRow dRow = dt.NewRow();

                    for (int col = 0; col < Columns.Count; col++)
                        dRow[col] = rows[row].Cells[col].Text.Trim();


                    dt.Rows.Add(dRow);
                }
            }

            DataView = dt.DefaultView;
        }

        #endregion
    }

    public enum ImageDataSource
    {
        File,
        Webcam,
        Lib
    }

    public enum ProcessingStages
    {
        Ready,       
        Processing,
        Completed,
        Error
    }
}