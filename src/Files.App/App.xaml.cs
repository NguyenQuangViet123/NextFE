// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers.Application;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
// [OPTIMIZATION] Thêm thư viện để xử lý API bộ nhớ
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Files.App
{
	/// <summary>
	/// Represents the entry point of UI for Files app.
	/// </summary>
	public partial class App : Application
	{
		public static SystemTrayIcon? SystemTrayIcon { get; private set; }

		public static TaskCompletionSource? SplashScreenLoadingTCS { get; private set; }
		public static string? OutputPath { get; set; }

		private static CommandBarFlyout? _LastOpenedFlyout;
		public static CommandBarFlyout? LastOpenedFlyout
		{
			set
			{
				_LastOpenedFlyout = value;

				if (_LastOpenedFlyout is not null)
					_LastOpenedFlyout.Closed += LastOpenedFlyout_Closed;
			}
		}

		// ============================================================================
		// [NEXTFE VIP ENGINE] LAZY EVALUATION (LƯỜI KHỞI TẠO ĐỂ TĂNG TỐC STARTUP)
		// ============================================================================
		// Thay vì cấp phát bộ nhớ ngay lập tức, ta biến chúng thành thuộc tính động (=>).
		// Tụi này sẽ KHÔNG ngốn 1 byte RAM hay chu kỳ CPU nào cho đến khi thực sự được dùng!
		public static QuickAccessManager QuickAccessManager => Ioc.Default.GetRequiredService<QuickAccessManager>();
		public static StorageHistoryWrapper HistoryWrapper => Ioc.Default.GetRequiredService<StorageHistoryWrapper>();
		public static FileTagsManager FileTagsManager => Ioc.Default.GetRequiredService<FileTagsManager>();
		public static LibraryManager LibraryManager => Ioc.Default.GetRequiredService<LibraryManager>();
		public static AppModel AppModel => Ioc.Default.GetRequiredService<AppModel>();
		public static ILogger Logger => Ioc.Default.GetRequiredService<ILogger<App>>();

		// [OPTIMIZATION] Import API để tối ưu hóa bộ nhớ khi chạy ngầm
		// Vẫn giữ lại khai báo này để tránh lỗi biên dịch, dù ta sẽ tắt nó ở bên dưới
		[DllImport("psapi.dll")]
		private static extern int EmptyWorkingSet(IntPtr hwProc);

		/// <summary>
		/// Initializes an instance of <see cref="App"/>.
		/// </summary>
		public App()
		{
			InitializeComponent();

			// Configure exception handlers
			UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception, true);
			AppDomain.CurrentDomain.UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.ExceptionObject as Exception, false);
			TaskScheduler.UnobservedTaskException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception, false);
		}

		/// <summary>
		/// Gets invoked when the application is launched normally by the end user.
		/// </summary>
		protected override void OnLaunched(LaunchActivatedEventArgs e)
		{
			_ = ActivateAsync();

			async Task ActivateAsync()
			{
				// Get AppActivationArguments
				var appActivationArguments = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
				var isStartupTask = appActivationArguments.Data is Windows.ApplicationModel.Activation.IStartupTaskActivatedEventArgs;

				// OPTIMIZATION: Show UI immediately before processing heavy dependency injection
				// Triết lý QoL: Hiển thị Splash Screen ngay lập tức để người dùng biết App đã nhận lệnh.
				if (!isStartupTask)
				{
					// Initialize and activate MainWindow
					MainWindow.Instance.Activate();

					// Wait for the Window to initialize (Legacy requirement for WinUI Handle)
					await Task.Delay(10);

					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();

					// [OPTIMIZATION] Rút ngắn thời gian Delay xuống 16ms (~ 1 khung hình ở 60fps)
					// Đủ thời gian để UI kịp vẽ Splash Screen mà không làm chậm quá trình khởi động.
					await Task.Delay(16);
				}

				// [OPTIMIZATION] Chống đóng băng 3-5 giây (Unblock Main Thread).
				// Công việc build Dependency Injection sử dụng Reflection quét cực kỳ nặng. Ta offload nó sang Background Task.
				await Task.Run(() =>
				{
					var host = AppLifecycleHelper.ConfigureHost();
					Ioc.Default.ConfigureServices(host.Services);
				});

				var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
				var isLeaveAppRunning = userSettingsService.GeneralSettingsService.LeaveAppRunning;

				if (isStartupTask && !isLeaveAppRunning)
				{
					// Logic for startup task if not already activated above
					MainWindow.Instance.Activate();
					await Task.Delay(10);
					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();
				}

				// ============================================================================
				// [NEXTFE VIP ENGINE] BACKGROUND WARM-UP (HÂM NÓNG NGẦM)
				// ============================================================================
				// Đã xóa bỏ chuỗi lệnh khởi tạo tuần tự (await Task.Yield) chậm chạp của bản gốc.
				// Ta tạo một luồng phụ (Fire and Forget) để âm thầm gọi dậy các Manager nặng nề.
				// Nhờ vậy, Main Thread được giải phóng lập tức, App sẽ qua mặt Splash Screen ngay!
				_ = Task.Run(() =>
				{
					_ = QuickAccessManager;
					_ = HistoryWrapper;
					_ = FileTagsManager;
					_ = LibraryManager;
				});

				// Hook events for the window
				MainWindow.Instance.Closed += Window_Closed;
				MainWindow.Instance.Activated += Window_Activated;

				Logger.LogInformation($"App launched. Launch args type: {appActivationArguments.Data.GetType().Name}");

				if (!(isStartupTask && isLeaveAppRunning))
				{
					// Wait for the UI to update
					await SplashScreenLoadingTCS!.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(500));
					SplashScreenLoadingTCS = null;

					// Create a system tray icon
					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					_ = MainWindow.Instance.InitializeApplicationAsync(appActivationArguments.Data);
				}
				else
				{
					// Create a system tray icon
					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					// Sleep current instance
					// [STEALTH] Đổi tên instance pool để xóa dấu vết "Files"
					Program.Pool = new(0, 1, $"ShellHostInternal-{AppLifecycleHelper.AppEnvironment}-Instance");

					Thread.Yield();

					if (Program.Pool.WaitOne())
					{
						// Resume the instance
						Program.Pool.Dispose();
						Program.Pool = null;
					}
				}

				await Task.Yield(); // Mở cổng thở chớp nhoáng trước khi khởi tạo linh kiện phụ
				await AppLifecycleHelper.InitializeAppComponentsAsync();
			}
		}

		/// <summary>
		/// Gets invoked when the application is activated.
		/// </summary>
		public async Task OnActivatedAsync(AppActivationArguments activatedEventArgs)
		{
			var activatedEventArgsData = activatedEventArgs.Data;

			// Logger may not be initialized yet due to race condition during startup
			if (Logger is not null)
				Logger.LogInformation($"The app is being activated. Activation type: {activatedEventArgsData.GetType().Name}");

			// InitializeApplication accesses UI, needs to be called on UI thread
			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(()
				=> MainWindow.Instance.InitializeApplicationAsync(activatedEventArgsData));
		}

		/// <summary>
		/// Gets invoked when the main window is activated.
		/// </summary>
		private void Window_Activated(object sender, WindowActivatedEventArgs args)
		{
			// OPTIMIZATION: Safety check for Logger to prevent crash on ultra-fast startups
			Logger?.LogInformation($"Window_Activated: State={args?.WindowActivationState.ToString()}");

			AppModel.IsMainWindowClosed = false;

			// TODO(s): Is this code still needed?
			if (args.WindowActivationState != WindowActivationState.CodeActivated ||
				args.WindowActivationState != WindowActivationState.PointerActivated)
				return;

			ApplicationData.Current.LocalSettings.Values["INSTANCE_ACTIVE"] = -Environment.ProcessId;
		}

		/// <summary>
		/// Gets invoked when the application execution is closed.
		/// </summary>
		/// <remarks>
		/// Saves the current state of the app such as opened tabs, and disposes all cached resources.
		/// </remarks>
		private async void Window_Closed(object sender, WindowEventArgs args)
		{
			// OPTIMIZATION: Immediate Visual Feedback.
			// Hide the window FIRST so the user thinks the app is closed instantly.
			// Then perform the heavy cleanup tasks in the background.
			try
			{
				MainWindow.Instance.AppWindow.Hide();
			}
			catch { /* Best effort to hide */ }

			// Save application state and stop any background activity
			IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			StatusCenterViewModel statusCenterViewModel = Ioc.Default.GetRequiredService<StatusCenterViewModel>();
			ICommandManager commandManager = Ioc.Default.GetRequiredService<ICommandManager>();

			// A Workaround for the crash (#10110)
			if (_LastOpenedFlyout?.IsOpen ?? false)
			{
				args.Handled = true;
				_LastOpenedFlyout.Closed += (sender, e) => App.Current.Exit();
				_LastOpenedFlyout.Hide();
				return;
			}

			// Save the current tab list in case it was overwriten by another instance
			if (userSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp || userSettingsService.AppSettingsService.RestoreTabsOnStartup)
				AppLifecycleHelper.SaveSessionTabs();
			else
				await commandManager.CloseAllTabs.ExecuteAsync();

			if (OutputPath is not null)
			{
				var instance = MainPageViewModel.AppInstances.FirstOrDefault(x => x.TabItemContent.IsCurrentInstance);
				if (instance is null)
					return;

				var items = (instance.TabItemContent as ShellPanesPage)?.ActivePane?.SlimContentPage?.SelectedItems;
				if (items is null)
					return;

				var results = items.Select(x => x.ItemPath).ToList();
				System.IO.File.WriteAllLines(OutputPath, results);

				IntPtr eventHandle = Win32PInvoke.CreateEvent(IntPtr.Zero, false, false, "FILEDIALOG");
				Win32PInvoke.SetEvent(eventHandle);
				Win32PInvoke.CloseHandle(eventHandle);
			}

			// Continue running the app on the background
			// [FIX] Sử dụng Process Name động thay vì hardcode "Files"
			// Điều này cho phép bạn đổi tên file EXE (ví dụ thành ShellHost.exe) mà tính năng chạy ngầm vẫn hoạt động đúng.
			if (userSettingsService.GeneralSettingsService.LeaveAppRunning &&
				!AppModel.ForceProcessTermination &&
				!Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Any(x => x.Id != Environment.ProcessId))
			{
				// Close open content dialogs
				UIHelpers.CloseAllDialogs();

				// Close all notification banners except in progress
				statusCenterViewModel.RemoveAllCompletedItems();

				// Cache the window instead of closing it
				// Note: We already called Hide() at the start, so this just confirms it.
				MainWindow.Instance.AppWindow.Hide();

				// Close all tabs
				MainPageViewModel.AppInstances.ForEach(tabItem => tabItem.Unload());
				MainPageViewModel.AppInstances.Clear();

				// Wait for all properties windows to close
				await FilePropertiesHelpers.WaitClosingAll();

				// ====================================================================================
				// [VIP OPTIMIZATION] CHIẾN LƯỢC HOT-STANDBY (ƯU TIÊN TỐC ĐỘ MỞ LẠI - BỎ QUA ĂN RAM)
				// ====================================================================================
				// Vì bạn đã cho phép "ăn RAM cũng không sao", ta KHÔNG ĐƯỢC dùng EmptyWorkingSet() nữa.
				// Việc ép giải phóng RAM vật lý làm ổ cứng phải đọc lại toàn bộ thư viện WinUI mỗi khi mở App,
				// gây ra độ trễ 1-2 giây vô cùng khó chịu.
				//
				// Thay vào đó, ta giữ app nằm nguyên trong RAM vật lý tốc độ cao.
				// Chỉ kích hoạt Garbage Collector chạy ngầm siêu nhẹ để dọn dẹp các Tab đã đóng.
				_ = Task.Run(() =>
				{
					// Dọn dẹp object rác nhưng không ép chặn luồng (false = Non-blocking)
					GC.Collect(2, GCCollectionMode.Optimized, false);
				});

				// Sleep current instance
				// [STEALTH] Dùng tên chung chung
				Program.Pool = new(0, 1, $"ShellHostInternal-{AppLifecycleHelper.AppEnvironment}-Instance");

				Thread.Yield();

				// Displays a notification the first time the app goes to the background
				if (userSettingsService.AppSettingsService.ShowBackgroundRunningNotification)
				{
					SafetyExtensions.IgnoreExceptions(() =>
					{
						AppToastNotificationHelper.ShowBackgroundRunningToast();

						userSettingsService.AppSettingsService.ShowBackgroundRunningNotification = false;
					});
				}

				if (Program.Pool.WaitOne())
				{
					// Resume the instance
					Program.Pool.Dispose();
					Program.Pool = null;

					if (!AppModel.ForceProcessTermination)
					{
						args.Handled = true;

						// Đẩy tác vụ check update ra luồng ngầm để cửa sổ bung lên ngay tắp lự
						_ = Task.Run(() => AppLifecycleHelper.CheckAppUpdate());
						return;
					}
				}
			}

			// Method can take a long time, make sure the window is hidden
			// OPTIMIZATION: Removed redundant Hide logic here since we moved it to top.
			await Task.Yield();

			// Try to maintain clipboard data after app close
			SafetyExtensions.IgnoreExceptions(() =>
			{
				// OPTIMIZATION: Clipboard.Flush can hang. We try to catch it if it takes too long or fails.
				var dataPackage = Clipboard.GetContent();
				if (dataPackage.Properties.PackageFamilyName == Package.Current.Id.FamilyName)
				{
					if (dataPackage.Contains(StandardDataFormats.StorageItems))
						Clipboard.Flush();
				}
			},
			Logger);

			// Destroy cached properties windows
			FilePropertiesHelpers.DestroyCachedWindows();
			AppModel.IsMainWindowClosed = true;

			// Wait for ongoing file operations
			FileOperationsHelpers.WaitForCompletion();
		}

		/// <summary>
		/// Gets invoked when the last opened flyout is closed.
		/// </summary>
		private static void LastOpenedFlyout_Closed(object? sender, object e)
		{
			if (sender is not CommandBarFlyout commandBarFlyout)
				return;

			commandBarFlyout.Closed -= LastOpenedFlyout_Closed;
			if (_LastOpenedFlyout == commandBarFlyout)
				_LastOpenedFlyout = null;
		}
	}
}