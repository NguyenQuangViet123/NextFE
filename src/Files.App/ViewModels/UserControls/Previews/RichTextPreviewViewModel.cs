// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class RichTextPreviewViewModel : BasePreviewModel
	{
		public IRandomAccessStream Stream { get; set; }

		public RichTextPreviewViewModel(ListedItem item) : base(item) { }

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Không mở luồng I/O xuống ổ cứng nếu người dùng đã lướt đi
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				Stream = await Item.ItemFile.OpenReadAsync();
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt gọn lỗi do hành động Hủy tác vụ
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			return [];
		}

		public override void PreviewControlBase_Unloaded(object sender, RoutedEventArgs e)
		{
			// [FIX] Cứu nguy lỗi "File is in Use" (Khóa file).
			// Nếu không Dispose Stream ở đây, file .rtf gốc sẽ bị khóa chặt trên đĩa.
			// Người dùng không thể Xóa, Đổi tên, hoặc Ghi đè file đó chừng nào ứng dụng chưa tắt.
			if (Stream != null)
			{
				Stream.Dispose();
				Stream = null;
			}

			base.PreviewControlBase_Unloaded(sender, e);
		}
	}
}