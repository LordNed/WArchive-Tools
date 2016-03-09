using GCMRebuilderUI.Commands;
using System;
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

        public bool IsFileLoaded { get; set; }

        private string m_windowTitle;

        public MainWindowViewModel()
        {
            UpdateWindowTitle();
        }

        private void OnUserRequestOpenImage()
        {
            throw new NotImplementedException();
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
