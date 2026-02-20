// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public abstract partial class BasePreviewModel : ObservableObject
	{
		private readonly IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

		public ListedItem Item { get; }

		private BitmapImage fileImage;
		public BitmapImage FileImage
		{
			get => fileImage;
			protected set => SetProperty(ref fileImage, value);
		}

		public List<FileProperty> DetailsFromPreview { get; set; }

		/// <summary>
		/// This is cancelled when the user has selected another file or closed the pane.
		/// </summary>
		public CancellationTokenSource LoadCancelledTokenSource { get; } = new CancellationTokenSource();

		public BasePreviewModel(ListedItem item) : base()
			=> Item = item;

		public delegate void LoadedEventHandler(object sender, EventArgs e);

		public static Task LoadDetailsOnlyAsync(ListedItem item, List<FileProperty> details = null)
		{
			var temp = new DetailsOnlyPreviewModel(item) { DetailsFromPreview = details };
			return temp.LoadAsync();
		}

		public static Task<string> ReadFileAsTextAsync(BaseStorageFile file, int maxLength = 10 * 1024 * 1024)
			=> file.ReadTextAsync(maxLength);

		/// <summary>
		/// Call this function when you are ready to load the preview and details.
		/// Override if you need custom loading code.
		/// </summary>
		/// <returns>The task to run</returns>
		public virtual async Task LoadAsync()
		{
			List<FileProperty> detailsFull = [];

			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Hủy ngay nếu người dùng đã chuyển sang file khác
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				if (Item.ItemFile is null)
				{
					var rootItem = await FilesystemTasks.Wrap(() => DriveHelpers.GetRootFromPathAsync(Item.ItemPath));

					LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
					Item.ItemFile = await StorageFileExtensions.DangerousGetFileFromPathAsync(Item.ItemPath, rootItem);
				}

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				await Task.Run(async () =>
				{
					// [OPTIMIZATION] Chốt chặn 2: Tránh sinh thêm task ngầm vô ích
					LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
					DetailsFromPreview = await LoadPreviewAndDetailsAsync();

					if (userSettingsService.InfoPaneSettingsService.SelectedTab == InfoPaneTabs.Details)
					{
						// Add the details from the preview function, then the system file properties
						DetailsFromPreview?.ForEach(i => detailsFull.Add(i));

						LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
						List<FileProperty> props = await GetSystemFilePropertiesAsync();
						if (props is not null)
						{
							detailsFull.AddRange(props);
						}
					}
				}, LoadCancelledTokenSource.Token);

				// [OPTIMIZATION] Chốt chặn 3: Tránh update UI nếu file này đã bị bỏ qua
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
				Item.FileDetails = new System.Collections.ObjectModel.ObservableCollection<FileProperty>(detailsFull);
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt gọn lỗi do người dùng chuyển file liên tục (Spam phím mũi tên)
				// Không làm crash app, chỉ đơn giản là bỏ qua tác vụ cũ.
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to load preview details.");
			}
		}

		/// <summary>
		/// Override this and place the code to load the file preview here.
		/// You can return details that may have been obtained while loading the preview (eg. word count).
		/// This details will be displayed *before* the system file properties.
		/// If there are none, return an empty list.
		/// </summary>
		/// <returns>A list of details</returns>
		public async virtual Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			// Kiểm tra hủy trước khi gọi WinRT Thumbnail siêu nặng
			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			var result = await FileThumbnailHelper.GetIconAsync(
				Item.ItemPath,
				Constants.ShellIconSizes.Jumbo,
				false,
				IconOptions.None);

			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			if (result is not null)
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () => FileImage = await result.ToBitmapAsync());
			else
				FileImage ??= await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => new BitmapImage());

			return [];
		}

		/// <summary>
		/// Override this if the preview control needs to handle the unloaded event.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		public virtual void PreviewControlBase_Unloaded(object sender, RoutedEventArgs e)
			=> LoadCancelledTokenSource.Cancel();

		protected static FileProperty GetFileProperty(string nameResource, object value)
			=> new() { NameResource = nameResource, Value = value };

		private async Task<List<FileProperty>> GetSystemFilePropertiesAsync()
		{
			if (Item.IsShortcut)
				return null;

			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			var list = await FileProperty.RetrieveAndInitializePropertiesAsync(Item.ItemFile,
				Constants.ResourceFilePaths.PreviewPaneDetailsPropertiesJsonPath);

			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			// [OPTIMIZATION] Khắc phục tình trạng treo Preview khi xem ảnh có GPS (Geocoding Trap).
			// Lệnh cũ: Đợi mạng trả về tên đường/quốc gia rồi mới cho hiện các thông tin khác -> CỰC KỲ LAG.
			// Lệnh mới: Hiển thị ngay lập tức. Tọa độ GPS sẽ được phân giải ngầm (Fire-and-forget), mạng chậm cũng không sao.
			var addressProp = list.Find(x => x.ID is "address");
			if (addressProp != null)
			{
				var latProp = list.Find(x => x.Property is "System.GPS.LatitudeDecimal");
				var lonProp = list.Find(x => x.Property is "System.GPS.LongitudeDecimal");

				if (latProp?.Value is double lat && lonProp?.Value is double lon)
				{
					_ = Task.Run(async () =>
					{
						try
						{
							var address = await LocationHelpers.GetAddressFromCoordinatesAsync(lat, lon);
							if (!string.IsNullOrEmpty(address))
							{
								await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
								{
									addressProp.Value = address;
								}, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
							}
						}
						catch { /* Bỏ qua nếu mất mạng hoặc lỗi API */ }
					}, LoadCancelledTokenSource.Token);
				}
			}

			// Adds the value for the file tag
			list.FirstOrDefault(x => x.ID is "filetag").Value =
				Item.FileTagsUI is not null ? string.Join(',', Item.FileTagsUI.Select(x => x.Name)) : null;

			return list.Where(i => i.ValueText is not null).ToList();
		}

		private sealed partial class DetailsOnlyPreviewModel : BasePreviewModel
		{
			public DetailsOnlyPreviewModel(ListedItem item) : base(item) { }

			public override Task<List<FileProperty>> LoadPreviewAndDetailsAsync() => Task.FromResult(DetailsFromPreview);
		}
	}
}