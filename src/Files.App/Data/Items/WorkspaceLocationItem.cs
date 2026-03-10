// Copyright (c) NextFE
// Licensed under the MIT License.

using Files.App.Data.Models.Workspaces;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Files.App.Data.Items
{
	/// <summary>
	/// [NEXTFE VIP ENGINE]
	/// Linh hồn đại diện cho thẻ Workspace trên thanh Sidebar.
	/// Kế thừa LocationItem để SidebarItem.cs tự động hiểu và vẽ giao diện.
	/// </summary>
	public class WorkspaceLocationItem : LocationItem
	{
		public WorkspaceItem Workspace { get; }

		public WorkspaceLocationItem(WorkspaceItem workspace)
		{
			Workspace = workspace;

			// Gắn thẻ ID để UI hiển thị tên
			Text = workspace.Name;

			// [HACK ĐIỀU HƯỚNG] Khi click vào Sidebar, nó sẽ nhảy đến đường dẫn ảo này
			Path = workspace.Id;

			// Không hiển thị dung lượng hay ghim trên Workspace
			IsDefaultLocation = false;

			// [FIX LỖI ICON] Sử dụng thuộc tính 'Icon' thay vì 'IconSource'
			// Ở đây tạm bỏ trống (null) để dùng Icon thư mục mặc định của hệ thống, tránh lỗi văng App do thiếu file SVG.
			Icon = null;

			// Lắng nghe sự thay đổi tên từ phía Manager để tự cập nhật UI
			Workspace.PropertyChanged += Workspace_PropertyChanged;
		}

		private void Workspace_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(WorkspaceItem.Name))
			{
				Text = Workspace.Name;
			}
		}
	}
}