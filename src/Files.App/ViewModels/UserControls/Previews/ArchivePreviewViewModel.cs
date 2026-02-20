// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using SevenZip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ArchivePreviewViewModel : BasePreviewModel
	{
		public ArchivePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh mở Stream nếu người dùng đã lướt qua file khác
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				using SevenZipExtractor zipFile = await FilesystemTasks.Wrap(async () =>
				{
					var stream = await Item.ItemFile.OpenStreamForReadAsync();

					// [OPTIMIZATION] Chốt chặn 2: Hủy ngay trước khi thư viện SevenZip băm file (rất nặng)
					LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

					var arch = new SevenZipExtractor(stream);

					// Force load archive (1665013614u)
					return arch?.ArchiveFileData is null ? null : arch;
				});

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				if (zipFile is null)
				{
					// Loads the thumbnail preview
					_ = await base.LoadPreviewAndDetailsAsync();

					return details;
				}

				//zipFile.IsStreamOwner = true;

				var folderCount = 0;
				var fileCount = 0;
				ulong totalSize = 0;

				// [OPTIMIZATION] Biến đếm để tránh gọi kiểm tra Hủy (Cancellation) quá nhiều làm chậm vòng lặp
				int checkCancelCounter = 0;

				foreach (ArchiveFileInfo entry in zipFile.ArchiveFileData)
				{
					// [OPTIMIZATION] Chốt chặn 3: Cứ đếm 100 file thì kiểm tra xem người dùng còn chờ không.
					// Tránh tình trạng ứng dụng bị treo (freeze) khi load file zip có > 50,000 files bên trong.
					if (checkCancelCounter++ % 100 == 0)
					{
						LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
					}

					if (!entry.IsDirectory)
					{
						++fileCount;
						totalSize += entry.Size;
					}
				}

				folderCount = (int)zipFile.FilesCount - fileCount;

				string propertyItemCount = Strings.DetailsArchiveItems.GetLocalizedFormatResource(zipFile.FilesCount, fileCount, folderCount);
				details.Add(GetFileProperty("PropertyItemCount", propertyItemCount));
				details.Add(GetFileProperty("PropertyUncompressedSize", totalSize.ToLongSizeString()));

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				// Loads the thumbnail preview
				_ = await base.LoadPreviewAndDetailsAsync();
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Bắt và nuốt gọn lỗi Cancelled để không văng Exception
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			return details;
		}
	}
}