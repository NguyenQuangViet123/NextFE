// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Files.App.Services.PreviewPopupProviders
{
	public sealed class PowerToysPeekProvider : IPreviewPopupProvider
	{
		public static PowerToysPeekProvider Instance { get; } = new();

		private static string? _peekExecutablePath;

		public async Task TogglePreviewPopupAsync(string path)
		{
			await DoPreviewAsync(path);
		}

		public async Task SwitchPreviewAsync(string path)
		{
			// Not used
			// [OPTIMIZATION] Thêm Task.CompletedTask để tránh cảnh báo CS1998 (Fake Async)
			await Task.CompletedTask;
		}

		private async Task DoPreviewAsync(string path)
		{
			if (_peekExecutablePath != null)
			{
				// ====================================================================================
				// [VIP OPTIMIZATION] OFFLOAD PROCESS.START
				// ====================================================================================
				// Process.Start() tốn thời gian gọi xuống API của OS. 
				// Đẩy nó vào Task.Run để luồng UI (Spacebar) phản hồi chớp nhoáng không bị khựng.
				await Task.Run(() =>
				{
					try
					{
						var psi = new ProcessStartInfo
						{
							FileName = _peekExecutablePath,
							Arguments = $"\"{path}\"",
							UseShellExecute = true,
							CreateNoWindow = true,
							WindowStyle = ProcessWindowStyle.Hidden
						};

						Process.Start(psi);
					}
					catch
					{
						// Ignore
					}
				});
			}
		}

		public async Task<bool> DetectAvailability()
		{
			// ====================================================================================
			// [OPTIMIZATION] ZERO-I/O CACHE HIT
			// ====================================================================================
			// Nếu đã tìm thấy đường dẫn từ lần kiểm tra trước, trả về ngay lập tức (O(1)).
			// Không đụng vào ổ cứng thêm lần nào nữa.
			if (_peekExecutablePath is not null)
				return true;

			// Đẩy việc quét thư mục xuống luồng nền để chống treo UI lúc khởi động.
			return await Task.Run(() =>
			{
				var exeName = "PowerToys.Peek.UI.exe";
				var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				var perUserPath = Path.Combine(localAppData, "PowerToys", "WinUI3Apps", exeName);

				// User path
				if (File.Exists(perUserPath))
				{
					_peekExecutablePath = perUserPath;
					return true;
				}

				// Machine-wide path
				string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
				string machinePath = Path.Combine(programFiles, "PowerToys", "WinUI3Apps", exeName);

				if (File.Exists(machinePath))
				{
					_peekExecutablePath = machinePath;
					return true;
				}

				// Not found
				return false;
			});
		}
	}
}