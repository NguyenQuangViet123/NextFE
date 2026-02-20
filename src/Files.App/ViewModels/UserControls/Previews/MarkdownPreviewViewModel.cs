// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class MarkdownPreviewViewModel : BasePreviewModel
	{
		private string textValue;
		public string TextValue
		{
			get => textValue;
			private set => SetProperty(ref textValue, value);
		}

		public MarkdownPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh đọc ổ cứng vô ích nếu người dùng đã spam phím lướt qua file khác
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var text = await ReadFileAsTextAsync(Item.ItemFile);

				// [OPTIMIZATION] Chốt chặn 2: Tránh cắt chuỗi và nhồi vào RAM của giao diện nếu tác vụ đã bị hủy
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt gọn lỗi Cancelled để tránh văng Exception rác ra Console
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			return [];
		}
	}
}