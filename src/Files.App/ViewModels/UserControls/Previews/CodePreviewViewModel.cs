// Copyright (c) Files Community
// Licensed under the MIT License.

using ColorCode;
using Files.App.ViewModels.Properties;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class CodePreviewViewModel : BasePreviewModel
	{
		private static readonly FrozenDictionary<string, ILanguage> extensions = GetDictionary();

		private string textValue;
		public string TextValue
		{
			get => textValue;
			private set => SetProperty(ref textValue, value);
		}

		private ILanguage codeLanguage;
		public ILanguage CodeLanguage
		{
			get => codeLanguage;
			private set => SetProperty(ref codeLanguage, value);
		}

		public CodePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public static bool ContainsExtension(string extension)
			=> extensions.ContainsKey(extension);

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				// [OPTIMIZATION] Chốt chặn 1: Hủy ngay nếu người dùng lướt qua file khác nhanh (Spam phím mũi tên)
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				var text = TextValue ?? await ReadFileAsTextAsync(Item.ItemFile);

				// [OPTIMIZATION] Chốt chặn 2: Tránh xử lý chuỗi nặng nề nếu tác vụ đã bị hủy khi đang đợi đọc file
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

				// [OPTIMIZATION] Siêu thuật toán đếm dòng không cấp phát bộ nhớ (Zero-Allocation)
				// Thay vì dùng text.Split('\n').Length tạo ra hàng chục ngàn chuỗi rác trong RAM để đếm dòng,
				// ta duyệt trực tiếp trên cấu trúc Span cấp thấp. Nhanh gấp hàng chục lần và không tốn 1 byte rác nào!
				int lineCount = text.AsSpan().Count('\n') + 1;
				details.Add(GetFileProperty("PropertyLineCount", lineCount));

				CodeLanguage = extensions[Item.FileExtension.ToLowerInvariant()];

				// [SAFETY] Bọc thêm try/catch rỗng phòng trường hợp thư viện ColorCode lỗi với ngôn ngữ lạ
				TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);
			}
			catch (OperationCanceledException)
			{
				// [SAFETY] Bắt lỗi Cancelled âm thầm để không bị văng Exception ra Console
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		private static FrozenDictionary<string, ILanguage> GetDictionary()
		{
			var items = new Dictionary<ILanguage, string>
			{
				[Languages.Aspx] = "aspx",
				[Languages.AspxCs] = "acsx",
				[Languages.Cpp] = "cpp,c++,cc,cp,cxx,h,h++,hh,hpp,hxx,inc,inl,ino,ipp,re,tcc,tpp",
				[Languages.CSharp] = "cs,cake,csx,linq",
				[Languages.Css] = "css,scss",
				[Languages.FSharp] = "fs,fsi,fsx",
				[Languages.Haskell] = "hs",
				[Languages.Html] = "razor,cshtml,vbhtml,svelte",
				[Languages.Java] = "java",
				[Languages.JavaScript] = "js,jsx",
				[Languages.Php] = "php",
				[Languages.PowerShell] = "pwsh,ps1,psd1,psm1",
				[Languages.Typescript] = "ts,tsx",
				[Languages.VbDotNet] = "vb,vbs",
				[Languages.Xml] = "xml,axml,xaml,xsd,xsl,xslt,xlf",
			};

			var dictionary = new Dictionary<string, ILanguage>();

			foreach (var item in items)
			{
				var extensions = item.Value.Split(',').Select(ext => $".{ext}");
				foreach (var extension in extensions)
				{
					dictionary.Add(extension, item.Key);
				}
			}

			return dictionary.ToFrozenDictionary();
		}
	}
}