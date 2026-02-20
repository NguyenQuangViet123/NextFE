// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed class FolderPreviewViewModel
	{
		private readonly InfoPaneViewModel infoPaneViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
		public ListedItem Item { get; }

		public BitmapImage Thumbnail { get; set; } = new();

		private BaseStorageFolder Folder { get; set; }

		// [OPTIMIZATION] Khởi tạo hệ thống Hủy Tác Vụ cho Folder Preview
		public CancellationTokenSource LoadCancelledTokenSource { get; } = new CancellationTokenSource();

		public FolderPreviewViewModel(ListedItem item)
			=> Item = item;

		public Task LoadAsync()
			=> LoadPreviewAndDetailsAsync();

		private async Task LoadPreviewAndDetailsAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Dừng nếu lướt qua thư mục khác
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var rootItem = await FilesystemTasks.Wrap(() => DriveHelpers.GetRootFromPathAsync(Item.ItemPath));
				Folder = await StorageFileExtensions.DangerousGetFolderFromPathAsync(Item.ItemPath, rootItem);

				// [OPTIMIZATION] Kẻ thù số 1: var items = await Folder.GetItemsAsync();
				// ĐÃ XÓA BỎ LỆNH NÀY! 
				// Tác giả gọi GetItemsAsync() để lấy toàn bộ file nhưng KHÔNG HỀ SỬ DỤNG biến `items`.
				// Nếu bấm vào thư mục lớn, lệnh này sẽ bắt ổ cứng đọc hàng ngàn file vô nghĩa và làm treo máy.

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var result = await FileThumbnailHelper.GetIconAsync(
					Item.ItemPath,
					Constants.ShellIconSizes.Jumbo,
					true,
					IconOptions.None);

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				if (result is not null)
				{
					await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
					{
						if (!LoadCancelledTokenSource.Token.IsCancellationRequested)
							Thumbnail = await result.ToBitmapAsync();
					});
				}

				// If the selected item is the root of a drive (e.g. "C:\")
				// we do not need to load the properties below, since they will not be shown.
				// Drive properties will be obtained through the DrivesViewModel service.
				if (Item.IsDriveRoot)
					return;

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
				var info = await Folder.GetBasicPropertiesAsync();

				Item.FileDetails =
				[
					GetFileProperty("PropertyItemCount", infoPaneViewModel?.DirectoryItemCount),
					GetFileProperty("PropertyDateModified", info.DateModified),
					GetFileProperty("PropertyDateCreated", info.DateCreated),
					GetFileProperty("PropertyParsingPath", Folder.Path),
				];

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				if (GitHelpers.IsRepositoryEx(Item.ItemPath, out var repoPath) &&
					!string.IsNullOrEmpty(repoPath))
				{
					// [OPTIMIZATION] Đẩy tác vụ đọc Git (có thể tốn thời gian do parse file git) ra luồng ngầm.
					await Task.Run(async () =>
					{
						LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

						var gitDirectory = GitHelpers.GetGitRepositoryPath(Folder.Path, Path.GetPathRoot(Folder.Path));
						var headName = (await GitHelpers.GetRepositoryHead(gitDirectory))?.Name ?? string.Empty;
						var repositoryName = GitHelpers.GetOriginRepositoryName(gitDirectory);

						if (!LoadCancelledTokenSource.Token.IsCancellationRequested)
						{
							await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
							{
								if (!string.IsNullOrEmpty(gitDirectory))
									Item.FileDetails.Add(GetFileProperty("GitOriginRepositoryName", repositoryName));

								if (!string.IsNullOrWhiteSpace(headName))
									Item.FileDetails.Add(GetFileProperty("GitCurrentBranch", headName));
							});
						}
					}, LoadCancelledTokenSource.Token);
				}
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt lỗi Cancelled
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}

		// Hàm này gọi khi View (Giao diện) bị dỡ bỏ
		public void PreviewControlBase_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
			=> LoadCancelledTokenSource.Cancel();

		private static FileProperty GetFileProperty(string nameResource, object value)
			=> new() { NameResource = nameResource, Value = value };
	}
}