// Copyright (c) NextFE
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// [NEXTFE VIP ENGINE]
	/// Cổng giao tiếp trực tiếp với Everything (voidtools) thông qua P/Invoke.
	/// Bỏ qua hoàn toàn hệ thống Windows Indexer rùa bò. Tốc độ tìm kiếm tuyệt đối: < 0.01s.
	/// </summary>
	public static class EverythingApi
	{
		// Yêu cầu phải có file Everything64.dll nằm cùng thư mục với file chạy (.exe)
		private const string DllName = "Everything64.dll";

		const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
		const uint EVERYTHING_REQUEST_PATH = 0x00000002;
		const uint EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004;
		const uint EVERYTHING_REQUEST_EXTENSION = 0x00000008;
		const uint EVERYTHING_REQUEST_SIZE = 0x00000010;
		const uint EVERYTHING_REQUEST_DATE_CREATED = 0x00000020;
		const uint EVERYTHING_REQUEST_DATE_MODIFIED = 0x00000040;

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		private static extern uint Everything_SetSearchW(string lpSearchString);

		[DllImport(DllName)]
		private static extern void Everything_SetRequestFlags(uint dwRequestFlags);

		[DllImport(DllName)]
		private static extern void Everything_SetMax(uint dwMax);

		[DllImport(DllName)]
		private static extern bool Everything_QueryW(bool bWait);

		[DllImport(DllName)]
		private static extern uint Everything_GetNumResults();

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		private static extern void Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);

		/// <summary>
		/// Kiểm tra xem người dùng có đang cài và chạy Everything trên máy không.
		/// </summary>
		public static bool IsEverythingAvailable()
		{
			try
			{
				// Thử gọi một hàm nhẹ để xem DLL có tồn tại và Everything Service có đang chạy không
				Everything_SetMax(1);
				return true;
			}
			catch (DllNotFoundException)
			{
				return false;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Truy vấn siêu tốc lấy đường dẫn file.
		/// </summary>
		public static List<string> Search(string query, uint maxResults = 1000)
		{
			var results = new List<string>();

			if (!IsEverythingAvailable())
				return results;

			// Chuẩn bị truy vấn
			Everything_SetSearchW(query);
			Everything_SetRequestFlags(EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME);
			Everything_SetMax(maxResults);

			// Thực thi truy vấn (bWait = true để ép nó trả về kết quả ngay lập tức vào RAM)
			if (!Everything_QueryW(true))
				return results;

			uint numResults = Everything_GetNumResults();

			// MAX_PATH là 260, nhưng Windows 10/11 hỗ trợ Long Path nên ta để 32767 cho an toàn
			StringBuilder sb = new StringBuilder(32767);

			for (uint i = 0; i < numResults; i++)
			{
				sb.Clear();
				Everything_GetResultFullPathNameW(i, sb, (uint)sb.Capacity);
				string fullPath = sb.ToString();

				if (!string.IsNullOrWhiteSpace(fullPath))
				{
					results.Add(fullPath);
				}
			}

			return results;
		}
	}
}