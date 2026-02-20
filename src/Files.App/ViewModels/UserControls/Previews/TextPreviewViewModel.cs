// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class TextPreviewViewModel : BasePreviewModel
	{
		private string textValue;
		public string TextValue
		{
			get => textValue;
			private set => SetProperty(ref textValue, value);
		}

		public TextPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh I/O vô ích nếu người dùng lướt đi nhanh
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var text = TextValue ?? await ReadFileAsTextAsync(Item.ItemFile);

				// [OPTIMIZATION] Chốt chặn 2: Đảm bảo không tốn CPU tính toán chuỗi lớn nếu đã bị Hủy
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				// [OPTIMIZATION] Zero-Allocation Strings (Tuyệt kỹ Không cấp phát RAM)
				// Thay thế 2 lệnh text.Split() cũ vì chúng tạo ra hàng trăm ngàn chuỗi rác trong RAM.
				var span = text.AsSpan();

				// 1. Đếm dòng trực tiếp trên Span (Nhanh gấp chục lần)
				int lineCount = span.Count('\n') + 1;
				details.Add(GetFileProperty("PropertyLineCount", lineCount));

				// 2. Đếm từ (Word Count) bằng vòng lặp O(N) không cấp phát RAM
				int wordCount = 0;
				bool inWord = false;
				for (int i = 0; i < span.Length; i++)
				{
					char c = span[i];
					if (c == ' ' || c == '\n' || c == '\r' || c == '\t')
					{
						inWord = false;
					}
					else if (!inWord)
					{
						wordCount++;
						inWord = true;
					}
				}
				details.Add(GetFileProperty("PropertyWordCount", wordCount));

				TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt lỗi do hành động Hủy tác vụ
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		public static async Task<TextPreview> TryLoadAsTextAsync(ListedItem item)
		{
			string extension = item.FileExtension?.ToLowerInvariant();
			if (ExcludedExtensions(extension) || item.FileSizeBytes is 0 or > Constants.PreviewPane.TryLoadAsTextSizeLimit)
				return null;

			try
			{
				item.ItemFile = await StorageFileExtensions.DangerousGetFileFromPathAsync(item.ItemPath);

				var text = await ReadFileAsTextAsync(item.ItemFile);
				bool isBinaryFile = text.Contains("\0\0\0\0", StringComparison.Ordinal);

				if (isBinaryFile)
					return null;

				var model = new TextPreviewViewModel(item) { TextValue = text };
				await model.LoadAsync();

				return new TextPreview(model);
			}
			catch
			{
				return null;
			}
		}

		private static bool ExcludedExtensions(string extension)
			=> extension is ".iso";
	}
}