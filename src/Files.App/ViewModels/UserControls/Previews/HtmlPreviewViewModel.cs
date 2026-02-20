// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class HtmlPreviewViewModel : BasePreviewModel
	{
		public HtmlPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public static bool ContainsExtension(string extension)
			=> extension is ".htm" or ".html" or ".svg";

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			// [OPTIMIZATION] Chốt chặn hủy tác vụ: Dù hàm này rỗng, vẫn phải kiểm tra xem người dùng đã lướt qua file khác chưa.
			// Tránh việc BasePreviewModel tiếp tục quá trình Load Properties tốn CPU ngay sau khi hàm rỗng này hoàn tất.
			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			return [];
		}
	}
}