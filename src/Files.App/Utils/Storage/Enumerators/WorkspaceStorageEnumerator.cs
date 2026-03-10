// Copyright (c) NextFE
// Licensed under the MIT License.

using Files.App.Data.Models.Workspaces;
using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage.Enumerators
{
	/// <summary>
	/// [NEXTFE VIP ENGINE]
	/// Bộ quét Thư mục Ảo (Virtual Folder Enumerator).
	/// Chịu trách nhiệm biến các đường dẫn tản mác trong Workspace thành một danh sách file thống nhất.
	/// </summary>
	public static class WorkspaceStorageEnumerator
	{
		public static async Task<List<ListedItem>> ListEntriesAsync(
			string workspaceId,
			CancellationToken cancellationToken,
			Func<List<ListedItem>, Task> intermediateAction)
		{
			var tempList = new List<ListedItem>();

			// 1. Lấy thông tin Workspace từ Manager
			var workspace = App.WorkspaceManager.Workspaces.FirstOrDefault(w => w.Id.Equals(workspaceId, StringComparison.OrdinalIgnoreCase));
			if (workspace == null || workspace.ItemPaths == null || !workspace.ItemPaths.Any())
			{
				return tempList; // Workspace trống hoặc không tồn tại
			}

			// 2. Quét từng file vật lý có trong danh sách
			foreach (var path in workspace.ItemPaths)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					// [OPTIMIZATION] Dùng Win32 API cấp thấp để đọc siêu tốc (Bypass WinRT)
					int additionalFlags = Win32PInvoke.FIND_FIRST_EX_LARGE_FETCH;
					IntPtr hFile = Win32PInvoke.FindFirstFileExFromApp(
						path,
						Win32PInvoke.FINDEX_INFO_LEVELS.FindExInfoBasic,
						out Win32PInvoke.WIN32_FIND_DATA findData,
						Win32PInvoke.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
						IntPtr.Zero,
						additionalFlags);

					if (hFile != IntPtr.Zero && hFile.ToInt64() != -1)
					{
						// File TỒN TẠI trên ổ cứng
						bool isFolder = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory;
						string parentPath = Path.GetDirectoryName(path) ?? string.Empty;

						ListedItem item = null;
						if (isFolder)
						{
							item = await Win32StorageEnumerator.GetFolder(findData, parentPath, false, cancellationToken);
						}
						else
						{
							item = await Win32StorageEnumerator.GetFile(findData, parentPath, false, cancellationToken);
						}

						if (item != null)
						{
							// Ghi đè lại đường dẫn tuyệt đối cho chắc chắn
							item.ItemPath = path;
							tempList.Add(item);
						}

						Win32PInvoke.FindClose(hFile);
					}
					else
					{
						// [UX UPGRADE] File MA (Dead Link)
						// Người dùng đã xóa file gốc ở ngoài Desktop nhưng trong Workspace vẫn còn lưu.
						// Ta sẽ tạo một File mờ ảo để báo lỗi thay vì crash App.
						var deadItem = new ListedItem(null)
						{
							PrimaryItemAttribute = StorageItemTypes.File,
							ItemNameRaw = Path.GetFileName(path),
							ItemPath = path,
							ItemType = "Liên kết hỏng (File gốc đã bị xóa)",
							Opacity = 0.4, // Làm mờ đi 60%
							NeedsPlaceholderGlyph = true,
							FileSize = "Không tìm thấy",
							ItemDateModifiedReal = DateTime.Now
						};
						tempList.Add(deadItem);
					}

					// 3. Đẩy dữ liệu lên UI dần dần (Streaming UI) để tạo cảm giác load tức thì
					if (tempList.Count % 10 == 0)
					{
						if (intermediateAction != null)
						{
							await intermediateAction(tempList);
							tempList.Clear();
						}
					}
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, $"Lỗi khi đọc file ảo trong Workspace: {path}");
				}
			}

			return tempList; // Trả về những file còn sót lại chưa được đẩy lên UI
		}
	}
}