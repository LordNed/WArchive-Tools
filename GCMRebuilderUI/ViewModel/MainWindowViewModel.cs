using GCMRebuilderUI.Commands;
using GCMRebuilderUI.Model;
using System.Windows;
using System.Windows.Input;

namespace GCMRebuilderUI.ViewModel
{
    public class MainWindowViewModel : ObservableObject
    { 
        public const string ApplicationName = "GCM Rebuilder";

        #region Commands
        /// <summary> The user has requested that open a new Disk Image. </summary>
        public ICommand OpenImage { get { return new RelayCommand(x => OnUserRequestOpenImage()); } }

        /// <summary> The user has requested that we close the application. </summary>
        public ICommand ExitApplication { get { return new RelayCommand(x => OnUserRequestExitApplication()); } }
        #endregion

        #region Misc
        public string WindowTitle
        {
            get { return m_windowTitle; }
            set
            {
                m_windowTitle = value;
                OnPropertyChanged("WindowTitle");
            }
        }
        #endregion

        public bool IsFileLoaded
        {
            get { return Image != null; }
        }

        public DiskImage Image
        {
            get { return m_diskImage; }
            set
            {
                m_diskImage = value;
                OnPropertyChanged("Image");
                OnPropertyChanged("IsFileLoaded");
            }
        }

        private string m_windowTitle;
        private DiskImage m_diskImage;

        public MainWindowViewModel()
        {
            UpdateWindowTitle();
        }

        private void OnUserRequestOpenImage()
        {
            Image = new DiskImage();
        }

        private void OnUserRequestExitApplication()
        {
            Application.Current.MainWindow.Close();
        }

        private void UpdateWindowTitle()
        {
            if(!IsFileLoaded)
            {
                WindowTitle = ApplicationName;
            }
            else
            {
                WindowTitle = string.Format("{0} - {1}", "<Not Implemented>", ApplicationName);
            }
        }
    }
}
