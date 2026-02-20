// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class MediaPreviewViewModel : BasePreviewModel
	{
		public event EventHandler TogglePlaybackRequested;

		private MediaSource source;
		public MediaSource Source
		{
			get => source;
			private set => SetProperty(ref source, value);
		}

		public MediaPreviewViewModel(ListedItem item) : base(item) { }

		public void TogglePlayback()
			=> TogglePlaybackRequested?.Invoke(this, null);

		public override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh tạo MediaSource (rất nặng) nếu người dùng đã spam phím lướt qua file khác
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				Source = MediaSource.CreateFromStorageFile(Item.ItemFile);
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt gọn lỗi Cancelled để tránh văng Exception rác
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			return Task.FromResult(new List<FileProperty>());
		}

		public override void PreviewControlBase_Unloaded(object sender, RoutedEventArgs e)
		{
			// [OPTIMIZATION] Bắt buộc giải phóng tài nguyên Unmanaged (Video/Audio handles) ngay lập tức.
			// Tác giả gốc chỉ gán = null, làm file bị "Khóa" (File in use) và treo RAM một lúc lâu chờ GC dọn.
			// Dùng Dispose() chặt đứt mọi liên kết tới file ổ cứng ngay khi vừa tắt Preview.
			if (Source != null)
			{
				Source.Dispose();
				Source = null;
			}

			base.PreviewControlBase_Unloaded(sender, e);
		}
	}
}