// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using System.IO;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Files.App.Utils.Shell
{
	/// <summary>
	/// Provides static helper for launching external executable files.
	/// </summary>
	public static class LaunchHelper
	{
		public unsafe static void LaunchSettings(string page)
		{
			using ComPtr<IApplicationActivationManager> pApplicationActivationManager = default;
			pApplicationActivationManager.CoCreateInstance(CLSID.CLSID_ApplicationActivationManager);

			pApplicationActivationManager.Get()->ActivateApplication(
				"windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
				page,
				ACTIVATEOPTIONS.AO_NONE,
				out _);
		}

		public static Task<bool> LaunchAppAsync(string application, string arguments, string workingDirectory)
		{
			return HandleApplicationLaunch(application, arguments, workingDirectory);
		}

		public static async Task<bool> RunCompatibilityTroubleshooterAsync(string filePath)
		{
			var tempPath = Path.GetTempPath();
			var compatibilityTroubleshooterAnswerFile = Path.Combine(tempPath, "CompatibilityTroubleshooterAnswerFile.xml");
			var xmlContent = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Answers Version=\"1.0\"><Interaction ID=\"IT_LaunchMethod\"><Value>CompatTab</Value></Interaction><Interaction ID=\"IT_BrowseForFile\"><Value>{filePath}</Value></Interaction></Answers>";

			try
			{
				// [OPTIMIZATION] Sử dụng Async I/O để ghi file tạm, giải phóng luồng giao diện
				await File.WriteAllTextAsync(compatibilityTroubleshooterAnswerFile, xmlContent);
			}
			catch (IOException)
			{
				// Try with a different file name
				try
				{
					compatibilityTroubleshooterAnswerFile = Path.Combine(tempPath, "CompatibilityTroubleshooterAnswerFile1.xml");
					await File.WriteAllTextAsync(compatibilityTroubleshooterAnswerFile, xmlContent);
				}
				catch { /* Ignore exceptions to preserve original silent fallback behavior */ }
			}

			return await HandleApplicationLaunch("MSDT.exe", $"/id PCWDiagnostic /af \"{compatibilityTroubleshooterAnswerFile}\"", "");
		}

		private static async Task<bool> HandleApplicationLaunch(string application, string arguments, string workingDirectory)
		{
			var currentWindows = Win32Helper.GetDesktopWindows();

			if (FileExtensionHelpers.IsVhdFile(application))
			{
				// Use PowerShell to mount Vhd Disk as this requires admin rights
				return await Win32Helper.MountVhdDisk(application);
			}

			try
			{
				using Process process = new Process();
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.FileName = application;

				// Show window if workingDirectory (opening terminal)
				process.StartInfo.CreateNoWindow = string.IsNullOrEmpty(workingDirectory);

				if (arguments == "RunAs")
				{
					process.StartInfo.UseShellExecute = true;
					process.StartInfo.Verb = "RunAs";

					if (FileExtensionHelpers.IsMsiFile(application))
					{
						process.StartInfo.FileName = "MSIEXEC.exe";
						process.StartInfo.Arguments = $"/a \"{application}\"";
					}
				}
				else if (arguments == "RunAsUser")
				{
					process.StartInfo.UseShellExecute = true;
					process.StartInfo.Verb = "RunAsUser";

					if (FileExtensionHelpers.IsMsiFile(application))
					{
						process.StartInfo.FileName = "MSIEXEC.exe";
						process.StartInfo.Arguments = $"/i \"{application}\"";
					}
				}
				else
				{
					process.StartInfo.Arguments = arguments;

					// ====================================================================================
					// [VIP OPTIMIZATION] OFFLOAD HEAVY REGISTRY I/O THREAD
					// ====================================================================================
					// Gọi Environment.GetEnvironmentVariables() yêu cầu quét Registry cực kỳ tốn thời gian.
					// Đưa vào Task.Run giúp UI (App) không bị đóng băng khi khởi chạy file.
					await Task.Run(() =>
					{
						var machineVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
						var userVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);

						foreach (DictionaryEntry ent in machineVars)
						{
							string key = (string)ent.Key;

							// Skip USERNAME to avoid issues where files were executed as SYSTEM user (#12139)
							if (string.Equals(key, "USERNAME", StringComparison.OrdinalIgnoreCase))
								continue;

							process.StartInfo.EnvironmentVariables[key] = (string)ent.Value;
						}

						foreach (DictionaryEntry ent in userVars)
						{
							process.StartInfo.EnvironmentVariables[(string)ent.Key] = (string)ent.Value;
						}

						// [OPTIMIZATION] Tái sử dụng Dictionary đã lấy ở trên, KHÔNG gọi GetEnvironmentVariable 
						// thêm 2 lần nữa để tránh việc Windows phải mở Registry thêm lần nào.
						process.StartInfo.EnvironmentVariables["PATH"] = string.Join(';',
							machineVars["PATH"] as string ?? string.Empty,
							userVars["PATH"] as string ?? string.Empty);
					});
				}

				process.StartInfo.WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? PathNormalization.GetParentDir(application) : workingDirectory;
				process.Start();

				Win32Helper.BringToForeground(currentWindows);

				return true;
			}
			catch (Win32Exception)
			{
				using Process process = new Process();
				process.StartInfo.UseShellExecute = true;
				process.StartInfo.FileName = application;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? PathNormalization.GetParentDir(application) : workingDirectory;

				try
				{
					process.Start();

					Win32Helper.BringToForeground(currentWindows);

					return true;
				}
				catch (Win32Exception ex) when (ex.NativeErrorCode == 50)
				{
					// ShellExecute return code 50 (ERROR_NOT_SUPPORTED) for some exes (#15179)
					return Win32Helper.RunPowershellCommand($"\"{application}\"", PowerShellExecutionOptions.Hidden);
				}
				catch (Win32Exception)
				{
					try
					{
						var opened = await STATask.Run(async () =>
						{
							// [OPTIMIZATION] Dùng mảng và loại bỏ phần tử rỗng trực tiếp thay vì đẻ IEnumerable
							var split = application.Split('|', StringSplitOptions.RemoveEmptyEntries);

							if (split.Length == 1)
							{
								Process.Start(GetMtpPath(split[0]));
								Win32Helper.BringToForeground(currentWindows);
							}
							else
							{
								var groups = split.Select(x => GetMtpPath(x))
												  .GroupBy(x => new
												  {
													  Dir = Path.GetDirectoryName(x),
													  Prog = Win32Helper.GetDefaultFileAssociationAsync(x).Result ?? Path.GetExtension(x)
												  });

								foreach (var group in groups)
								{
									using var cMenu = await ContextMenu.GetContextMenuForFiles(group.ToArray(), PInvoke.CMF_DEFAULTONLY);

									if (cMenu is not null)
										await cMenu.InvokeVerb(Shell32.CMDSTR_OPEN);
								}
							}

							return true;
						}, App.Logger);

						if (!opened && application.StartsWith(@"\\SHELL\", StringComparison.Ordinal))
						{
							opened = await STATask.Run(async () =>
							{
								using var cMenu = await ContextMenu.GetContextMenuForFiles(new[] { application }, PInvoke.CMF_DEFAULTONLY);

								if (cMenu is not null)
									await cMenu.InvokeItem(cMenu.Items.FirstOrDefault()?.ID ?? -1);

								return true;
							}, App.Logger);
						}

						if (!opened)
						{
							var isAlternateStream = RegexHelpers.AlternateStream().IsMatch(application);
							if (isAlternateStream)
							{
								// [OPTIMIZATION] Thay thế cụm LINQ nặng nề bằng IndexOf và Substring (O(1) memory)
								var fileName = Path.GetFileName(application);
								int colonIndex = fileName.IndexOf(':');
								string streamName = colonIndex >= 0 && colonIndex + 1 < fileName.Length
									? fileName.Substring(colonIndex + 1)
									: fileName;

								var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
								Kernel32.CreateDirectory(basePath);

								var tempPath = Path.Combine(basePath, streamName);
								using var hFileSrc = Kernel32.CreateFile(application, Kernel32.FileAccess.GENERIC_READ, FileShare.ReadWrite, null, FileMode.Open, FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL);
								using var hFileDst = Kernel32.CreateFile(tempPath, Kernel32.FileAccess.GENERIC_WRITE, 0, null, FileMode.Create, FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL | FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY);

								if (!hFileSrc.IsInvalid && !hFileDst.IsInvalid)
								{
									// Copy ADS to temp folder and open
									await using (var inStream = new FileStream(hFileSrc.DangerousGetHandle(), FileAccess.Read))
									await using (var outStream = new FileStream(hFileDst.DangerousGetHandle(), FileAccess.Write))
									{
										await inStream.CopyToAsync(outStream);
										await outStream.FlushAsync();
									}

									opened = await HandleApplicationLaunch(tempPath, arguments, workingDirectory);
								}
							}
						}

						return opened;
					}
					catch (Win32Exception)
					{
						return false;
					}
					catch (ArgumentException)
					{
						return false;
					}
				}
			}
			catch (InvalidOperationException)
			{
				return false;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, $"Error launching: {application}");
				return false;
			}
		}

		private static string GetMtpPath(string executable)
		{
			const string mtpPrefix = "\\\\?\\";
			if (executable.StartsWith(mtpPrefix, StringComparison.Ordinal))
			{
				using var computer = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_ComputerFolder);

				// [OPTIMIZATION] Dùng Substring thay vì .Replace tạo ra nhiều allocations trung gian
				string cleanPath = executable.Substring(mtpPrefix.Length);

				using var device = computer.FirstOrDefault(i => cleanPath.StartsWith(i.Name, StringComparison.Ordinal));
				var deviceId = device?.ParsingName;
				var itemPath = RegexHelpers.WindowsPath().Replace(executable, "");

				return deviceId is not null ? Path.Combine(deviceId, itemPath) : executable;
			}

			return executable;
		}
	}
}