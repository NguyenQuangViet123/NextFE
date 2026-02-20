// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ImagePreviewViewModel : BasePreviewModel
	{
		private ImageSource imageSource;
		public ImageSource ImageSource
		{
			get => imageSource;
			private set => SetProperty(ref imageSource, value);
		}

		public ImagePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		// TODO: Use existing helper mothods
		public static bool ContainsExtension(string extension)
			=> extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tiff" or ".ico" or ".webp" or ".jxr";

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh mở luồng I/O (đọc ổ cứng) vô ích nếu người dùng đã lướt đi
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				using IRandomAccessStream stream = await Item.ItemFile.OpenAsync(FileAccessMode.Read);

				// [OPTIMIZATION] Chốt chặn 2: Tránh đẩy tác vụ giải mã ảnh nặng nề lên luồng UI
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					// [OPTIMIZATION] Chốt chặn 3 (Trên UI Thread): Cú chót trước khi thực sự cấp phát RAM cho Bitmap
					if (LoadCancelledTokenSource.Token.IsCancellationRequested) return;

					BitmapImage bitmap = new();
					await bitmap.SetSourceAsync(stream);
					ImageSource = bitmap;
				});
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Bắt lỗi Cancelled âm thầm để không văng Exception ra ngoài
			}
			catch (Exception)
			{
				// Bỏ qua lỗi nếu file ảnh bị hỏng hoặc luồng đọc gặp sự cố
			}

			return [];
		}
	}
}