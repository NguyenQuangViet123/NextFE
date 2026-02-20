// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	internal sealed partial class ShortcutPreviewViewModel : BasePreviewModel
	{
		public ShortcutPreviewViewModel(ListedItem item) : base(item) { }

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var item = Item as IShortcutItem;
			var details = new List<FileProperty>
			{
				GetFileProperty("PropertyParsingPath", item.ItemPath),
				GetFileProperty("PropertyItemName", item.Name),
				GetFileProperty("PropertyItemTypeText", item.ItemType),
				GetFileProperty("PropertyItemTarget", item.TargetPath),
				GetFileProperty("Arguments", item.Arguments),
			};

			// [OPTIMIZATION] Chốt chặn trước khi tải icon nặng
			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			await LoadItemThumbnailAsync();

			return details;
		}

		public override async Task LoadAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh thực thi nếu người dùng lướt đi quá nhanh
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var details = await LoadPreviewAndDetailsAsync();

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				Item.FileDetails?.Clear();
				Item.FileDetails = new(details.OfType<FileProperty>());
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Bắt lỗi Cancelled âm thầm để không văng Exception
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}

		private async Task LoadItemThumbnailAsync()
		{
			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			// [DANGER ZONE] Hàm lấy Icon cho Shortcut rất dễ gây treo máy nếu Target của Shortcut
			// nằm trên ổ cứng ngủ hoặc mạng LAN bị đứt. Bắt buộc phải có khả năng hủy.
			var result = await FileThumbnailHelper.GetIconAsync(
				Item.ItemPath,
				Constants.ShellIconSizes.Jumbo,
				false,
				IconOptions.None);

			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			if (result is not null)
			{
				// [SAFETY] Đẩy tác vụ map ảnh lên UI Thread và kiểm tra lần cuối trước khi cập nhật
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					if (!LoadCancelledTokenSource.Token.IsCancellationRequested)
						FileImage = await result.ToBitmapAsync();
				});
			}
		}
	}
}