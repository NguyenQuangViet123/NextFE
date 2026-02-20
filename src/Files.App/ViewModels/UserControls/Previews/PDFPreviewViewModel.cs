// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class PDFPreviewViewModel : BasePreviewModel
	{
		private Visibility loadingBarVisibility;
		public Visibility LoadingBarVisibility
		{
			get => loadingBarVisibility;
			private set => SetProperty(ref loadingBarVisibility, value);
		}

		// The pips pager will crash when binding directly to Pages.Count, so count the pages here
		private int pageCount;
		public int PageCount
		{
			get => pageCount;
			set => SetProperty(ref pageCount, value);
		}

		public ObservableCollection<PageViewModel> Pages { get; } = [];

		public PDFPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public static bool ContainsExtension(string extension)
			=> extension is ".pdf";

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();
			IRandomAccessStream fileStream = null;

			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Tránh mở luồng đọc file ổ cứng nếu người dùng lướt đi quá nhanh
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				fileStream = await Item.ItemFile.OpenReadAsync();

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var pdf = await PdfDocument.LoadFromStreamAsync(fileStream);

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				// Add the number of pages to the details
				details.Add(GetFileProperty("PropertyPageCount", pdf.PageCount));

				// Chuyển giao quyền quản lý fileStream cho TryLoadPagesAsync
				_ = TryLoadPagesAsync(pdf, fileStream);

				// Gán bằng null để không bị Dispose ở block finally bên dưới (do TryLoadPagesAsync sẽ tự dọn)
				fileStream = null;
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Nuốt lỗi do hành động Hủy tác vụ
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}
			finally
			{
				// [FIX] Sửa lỗi rò rỉ File Lock: Nếu PdfDocument.LoadFromStreamAsync ném lỗi hoặc bị hủy sớm, 
				// fileStream sẽ bị treo vĩnh viễn ở bản gốc. Ở đây ta chủ động dọn dẹp nếu có lỗi.
				fileStream?.Dispose();
			}

			return details;
		}

		public async Task TryLoadPagesAsync(PdfDocument pdf, IRandomAccessStream fileStream)
		{
			try
			{
				await LoadPagesAsync(pdf);
			}
			catch (OperationCanceledException)
			{
				// Nuốt lỗi hủy
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}
			finally
			{
				fileStream.Dispose();
			}
		}

		private async Task LoadPagesAsync(PdfDocument pdf)
		{
			// This fixes an issue where loading an absurdly large PDF would take to much RAM and eventually cause a crash
			var limit = Math.Clamp(pdf.PageCount, 0, Constants.PreviewPane.PDFPageLimit);

			for (uint i = 0; i < limit; i++)
			{
				// [OPTIMIZATION] Dừng lại ngay lập tức nếu người dùng đã hủy (Spam phím mũi tên)
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				// [CRITICAL FIX] Memory Leak (Tràn RAM Native): Lớp PdfPage là Unmanaged/COM object cực nặng. 
				// Mã gốc không Dispose nó, dẫn đến việc lướt qua PDF nhiều trang sẽ làm RAM vọt lên hàng chục GB.
				// Thêm `using` sẽ lập tức giải phóng VRAM ngay sau khi render xong ảnh trang đó!
				using PdfPage page = pdf.GetPage(i);
				await page.PreparePageAsync();

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
				await page.RenderToStreamAsync(stream);

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
				using SoftwareBitmap sw = await decoder.GetSoftwareBitmapAsync();

				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					if (LoadCancelledTokenSource.Token.IsCancellationRequested) return;

					BitmapImage src = new();
					PageViewModel pageData = new()
					{
						PageImage = src,
						PageNumber = (int)i,
						PageImageSB = sw,
					};

					await src.SetSourceAsync(stream);
					Pages.Add(pageData);

					++PageCount;
				});
			}

			if (!LoadCancelledTokenSource.Token.IsCancellationRequested)
			{
				// [SAFETY] Đưa việc ẩn LoadingBar vào UI Thread để tránh lỗi COMException ở các bản Windows mới
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					LoadingBarVisibility = Visibility.Collapsed;
				});
			}
		}
	}

	public struct PageViewModel
	{
		public int PageNumber { get; set; }

		public BitmapImage PageImage { get; set; }

		public SoftwareBitmap PageImageSB { get; set; }
	}
}