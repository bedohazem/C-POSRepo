using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using POS_System.Windows;
using POS_System.Localization;

namespace POS_System
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // مهم: خلي التطبيق مايقفلش قبل ما نكمل
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Global exception handlers
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            try
            {
                // DB Init
                Database.Init();

                base.OnStartup(e);

                // Localization
                var saved = AppState.LoadCulture("ar-EG");
                Loc.I.SetCulture(saved);

                // Login
                var login = new LoginWindow();
                var ok = login.ShowDialog();

                if (ok != true)
                {
                    Shutdown();
                    return;
                }

                // Main
                var main = new MainWindow();
                MainWindow = main;
                main.Show();

                // تطبيق الثقافة بعد فتح MainWindow
                Loc.I.SetCulture(saved);

                try
                {
                    main.NavigateToHome();
                }
                catch
                {
                    // لو MainWindow بيعمل navigate لوحده، تجاهل
                }

                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Startup failed");
                MessageBox.Show(
                    "حدث خطأ أثناء بدء تشغيل البرنامج.\nتم تسجيل الخطأ في ملف السجل.",
                    "Crash",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CrashLogger.Log(e.Exception, "Unhandled UI exception");

            MessageBox.Show(
                "حصل خطأ غير متوقع وتم تسجيله في ملف الأخطاء.",
                "Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // نخليه Handled مؤقتًا عشان البرنامج مايقفلش فجأة
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                CrashLogger.Log(ex, "Non-UI unhandled exception");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            CrashLogger.Log(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        }
    }
}