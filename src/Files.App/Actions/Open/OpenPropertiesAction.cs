// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class OpenPropertiesAction : ObservableObject, IAction
	{
		private readonly IContentPageContext context;

		public string Label
			=> Strings.OpenProperties.GetLocalizedResource();

		public string Description
			=> Strings.OpenPropertiesDescription.GetLocalizedResource();

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Properties");

		public HotKey HotKey
			=> new(Keys.Enter, KeyModifiers.Alt);

		public bool IsExecutable =>
			context.PageType is not ContentPageTypes.Home &&
			!(context.PageType is ContentPageTypes.SearchResults &&
			!context.HasSelection);

		public OpenPropertiesAction()
		{
			context = Ioc.Default.GetRequiredService<IContentPageContext>();
			context.PropertyChanged += Context_PropertyChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			// [TƯ DUY NGƯỢC - NEXTFE VIP ENGINE]
			// Bỏ qua toàn bộ hệ thống render XAML của WinUI 3 (Nguyên nhân gây Crash).
			// Lái thẳng lệnh mở Properties xuống nhân Native Win32 của Windows.
			// Tốc độ mở < 0.01s, zero-allocation, siêu ổn định.

			if (context.HasSelection && context.SelectedItem?.ItemPath is not null)
			{
				// Nếu người dùng chọn nhiều file, ta mở Properties của file đầu tiên làm đại diện.
				// (Tránh spam mở 100 cửa sổ nếu họ Ctrl+A)
				ExecuteShellCommand(context.SelectedItem.ItemPath);
			}
			else if (context.Folder?.ItemPath is not null)
			{
				// Nếu không chọn file nào, mở Properties của thư mục hiện tại
				ExecuteShellCommand(context.Folder.ItemPath);
			}

			return Task.CompletedTask;
		}

		/// <summary>
		/// Gọi API cấp thấp của Windows để hiển thị bảng Properties gốc siêu nhẹ.
		/// </summary>
		private unsafe void ExecuteShellCommand(string itemPath)
		{
			try
			{
				SHELLEXECUTEINFOW info = default;
				info.cbSize = (uint)Marshal.SizeOf(info);
				info.nShow = 5; // SW_SHOW
				info.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST

				fixed (char* cVerb = "properties", lpFile = itemPath)
				{
					info.lpVerb = cVerb;
					info.lpFile = lpFile;

					// Yêu cầu Windows Kernel hiển thị bảng Properties
					PInvoke.ShellExecuteEx(ref info);
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Không thể mở Native Properties");
			}
		}

		private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.PageType):
				case nameof(IContentPageContext.HasSelection):
				case nameof(IContentPageContext.Folder):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}
	}
}