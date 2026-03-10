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
using System.Linq;
using System.Collections.Generic;
// [NEXTFE] Khai báo Workspaces
using Files.App.Data.Models.Workspaces;

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
		// [NEXTFE VIP ENGINE] LAZY EVALUATION & CACHING (O(1) THAY VÌ O(N))
		// ============================================================================
		// Giải thích: Bản gốc dùng "=> Ioc.Default..." sẽ bắt hệ thống quét Dependency Injection 
		// MỖI LẦN gọi tới. Việc này tốn CPU.
		// Tối ưu: Sử dụng Backing Fields và toán tử ??=. Khi Background Warm-up chạy, 
		// nó sẽ Resolve 1 lần duy nhất và gán vào các biến _*. Các lần gọi sau chỉ lấy trực tiếp từ RAM!
		private static QuickAccessManager? _quickAccessManager;
		public static QuickAccessManager QuickAccessManager => _quickAccessManager ??= Ioc.Default.GetRequiredService<QuickAccessManager>();

		private static StorageHistoryWrapper? _historyWrapper;
		public static StorageHistoryWrapper HistoryWrapper => _historyWrapper ??= Ioc.Default.GetRequiredService<StorageHistoryWrapper>();

		private static FileTagsManager? _fileTagsManager;
		public static FileTagsManager FileTagsManager => _fileTagsManager ??= Ioc.Default.GetRequiredService<FileTagsManager>();

		private static LibraryManager? _libraryManager;
		public static LibraryManager LibraryManager => _libraryManager ??= Ioc.Default.GetRequiredService<LibraryManager>();

		private static WorkspaceManager? _workspaceManager;
		public static WorkspaceManager WorkspaceManager => _workspaceManager ??= Ioc.Default.GetRequiredService<WorkspaceManager>();

		private static AppModel? _appModel;
		public static AppModel AppModel => _appModel ??= Ioc.Default.GetRequiredService<AppModel>();

		private static ILogger? _logger;
		public static ILogger Logger => _logger ??= Ioc.Default.GetRequiredService<ILogger<App>>();

		// [OPTIMIZATION] Import API để tối ưu hóa bộ nhớ khi chạy ngầm
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
				if (!isStartupTask)
				{
					// Initialize and activate MainWindow
					MainWindow.Instance.Activate();

					// Wait for the Window to initialize (Legacy requirement for WinUI Handle)
					await Task.Delay(10);

					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();

					// [OPTIMIZATION] Rút ngắn thời gian Delay xuống 16ms (~ 1 khung hình ở 60fps)
					await Task.Delay(16);
				}

				// [OPTIMIZATION] Chống đóng băng 3-5 giây (Unblock Main Thread).
				await Task.Run(() =>
				{
					var host = AppLifecycleHelper.ConfigureHost();
					Ioc.Default.ConfigureServices(host.Services);
				});

				var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
				var isLeaveAppRunning = userSettingsService.GeneralSettingsService.LeaveAppRunning;

				if (isStartupTask && !isLeaveAppRunning)
				{
					MainWindow.Instance.Activate();
					await Task.Delay(10);
					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();
				}

				// ============================================================================
				// [NEXTFE VIP ENGINE] BACKGROUND WARM-UP (HÂM NÓNG NGẦM)
				// ============================================================================
				// Luồng phụ này sẽ kích hoạt cơ chế toán tử ??= ở trên. Lưu cache thẳng vào RAM.
				_ = Task.Run(() =>
				{
					_ = QuickAccessManager;
					_ = HistoryWrapper;
					_ = FileTagsManager;
					_ = LibraryManager;
					_ = WorkspaceManager;
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

					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					_ = MainWindow.Instance.InitializeApplicationAsync(appActivationArguments.Data);
				}
				else
				{
					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					// [STEALTH] Đổi tên instance pool
					Program.Pool = new(0, 1, $"ShellHostInternal-{AppLifecycleHelper.AppEnvironment}-Instance");

					// [OPTIMIZATION] Không dùng Thread.Yield() trên UI thread nếu có thể, 
					// nhưng giữ nguyên để bảo toàn logic đồng bộ hóa IPC của ứng dụng gốc.
					Thread.Yield();

					if (Program.Pool.WaitOne())
					{
						Program.Pool.Dispose();
						Program.Pool = null;
					}
				}

				await Task.Yield();
				await AppLifecycleHelper.InitializeAppComponentsAsync();
			}
		}

		/// <summary>
		/// Gets invoked when the application is activated.
		/// </summary>
		public async Task OnActivatedAsync(AppActivationArguments activatedEventArgs)
		{
			var activatedEventArgsData = activatedEventArgs.Data;

			if (Logger is not null)
				Logger.LogInformation($"The app is being activated. Activation type: {activatedEventArgsData.GetType().Name}");

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(()
				=> MainWindow.Instance.InitializeApplicationAsync(activatedEventArgsData));
		}

		/// <summary>
		/// Gets invoked when the main window is activated.
		/// </summary>
		private void Window_Activated(object sender, WindowActivatedEventArgs args)
		{
			Logger?.LogInformation($"Window_Activated: State={args?.WindowActivationState.ToString()}");

			AppModel.IsMainWindowClosed = false;

			if (args.WindowActivationState != WindowActivationState.CodeActivated ||
				args.WindowActivationState != WindowActivationState.PointerActivated)
				return;

			ApplicationData.Current.LocalSettings.Values["INSTANCE_ACTIVE"] = -Environment.ProcessId;
		}

		/// <summary>
		/// Gets invoked when the application execution is closed.
		/// </summary>
		private async void Window_Closed(object sender, WindowEventArgs args)
		{
			// OPTIMIZATION: Immediate Visual Feedback.
			try
			{
				MainWindow.Instance.AppWindow.Hide();
			}
			catch { /* Best effort to hide */ }

			IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			StatusCenterViewModel statusCenterViewModel = Ioc.Default.GetRequiredService<StatusCenterViewModel>();
			ICommandManager commandManager = Ioc.Default.GetRequiredService<ICommandManager>();

			if (_LastOpenedFlyout?.IsOpen ?? false)
			{
				args.Handled = true;
				_LastOpenedFlyout.Closed += (sender, e) => App.Current.Exit();
				_LastOpenedFlyout.Hide();
				return;
			}

			if (userSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp || userSettingsService.AppSettingsService.RestoreTabsOnStartup)
				AppLifecycleHelper.SaveSessionTabs();
			else
				await commandManager.CloseAllTabs.ExecuteAsync();

			if (OutputPath is not null)
			{
				var instance = MainPageViewModel.AppInstances.FirstOrDefault(x => x.TabItemContent.IsCurrentInstance);
				if (instance is not null)
				{
					var items = (instance.TabItemContent as ShellPanesPage)?.ActivePane?.SlimContentPage?.SelectedItems;
					if (items is not null)
					{
						// [FIX] Cứu lỗi biên dịch: Phục hồi lại LINQ gốc thay vì gán Capacity cho List 
						// (do List Item có thể mang kiểu IEnumerable không hỗ trợ .Count dẫn đến đứt gãy XAML compiler).
						var results = items.Select(x => x.ItemPath).ToList();

						// [OPTIMIZATION] Dùng Async I/O để không block tiến trình tắt
						await System.IO.File.WriteAllLinesAsync(OutputPath, results);

						IntPtr eventHandle = Win32PInvoke.CreateEvent(IntPtr.Zero, false, false, "FILEDIALOG");
						Win32PInvoke.SetEvent(eventHandle);
						Win32PInvoke.CloseHandle(eventHandle);
					}
				}
			}

			// [OPTIMIZATION] Sửa rò rỉ bộ nhớ nghiêm trọng (Handle Leak). 
			// Dùng LINQ .Any() trên mảng Process[] sẽ làm rò rỉ Handle hệ thống do không gọi Dispose().
			bool hasOtherInstances = false;
			int currentProcessId = Environment.ProcessId;
			string processName = Process.GetCurrentProcess().ProcessName;

			var runningProcesses = Process.GetProcessesByName(processName);
			foreach (var p in runningProcesses)
			{
				if (p.Id != currentProcessId)
				{
					hasOtherInstances = true;
				}
				p.Dispose(); // Bắt buộc phải giải phóng OS Handle!
			}

			if (userSettingsService.GeneralSettingsService.LeaveAppRunning &&
				!AppModel.ForceProcessTermination &&
				!hasOtherInstances)
			{
				UIHelpers.CloseAllDialogs();
				statusCenterViewModel.RemoveAllCompletedItems();

				MainWindow.Instance.AppWindow.Hide();

				MainPageViewModel.AppInstances.ForEach(tabItem => tabItem.Unload());
				MainPageViewModel.AppInstances.Clear();

				await FilePropertiesHelpers.WaitClosingAll();

				// ====================================================================================
				// [VIP OPTIMIZATION] CHIẾN LƯỢC HOT-STANDBY
				// ====================================================================================
				_ = Task.Run(() =>
				{
					GC.Collect(2, GCCollectionMode.Optimized, false);
				});

				Program.Pool = new(0, 1, $"ShellHostInternal-{AppLifecycleHelper.AppEnvironment}-Instance");

				Thread.Yield();

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
					Program.Pool.Dispose();
					Program.Pool = null;

					if (!AppModel.ForceProcessTermination)
					{
						args.Handled = true;

						_ = Task.Run(() => AppLifecycleHelper.CheckAppUpdate());
						return;
					}
				}
			}

			await Task.Yield();

			// [OPTIMIZATION] Tránh treo App do Clipboard bị khóa bởi ứng dụng khác. 
			// Chuyển việc Flush sang luồng nền, App có thể tắt lẹ lập tức.
			SafetyExtensions.IgnoreExceptions(() =>
			{
				var dataPackage = Clipboard.GetContent();
				if (dataPackage?.Properties?.PackageFamilyName == Package.Current.Id.FamilyName)
				{
					if (dataPackage.Contains(StandardDataFormats.StorageItems))
					{
						// [FIX] Cứu lỗi biên dịch: Hàm IgnoreExceptions phải truyền đủ tham số Logger.
						Task.Run(() => SafetyExtensions.IgnoreExceptions(() => Clipboard.Flush(), Logger));
					}
				}
			},
			Logger);

			FilePropertiesHelpers.DestroyCachedWindows();
			AppModel.IsMainWindowClosed = true;

			FileOperationsHelpers.WaitForCompletion();
		}

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