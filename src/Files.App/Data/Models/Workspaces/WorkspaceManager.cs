// Copyright (c) NextFE
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Files.App.Data.Models.Workspaces
{
	/// <summary>
	/// Mô hình dữ liệu đại diện cho một Thư mục Ảo (Workspace)
	/// </summary>
	public class WorkspaceItem : ObservableObject
	{
		private string _id;
		public string Id
		{
			get => _id;
			set => SetProperty(ref _id, value);
		}

		private string _name;
		public string Name
		{
			get => _name;
			set => SetProperty(ref _name, value);
		}

		private string _icon;
		public string Icon
		{
			get => _icon;
			set => SetProperty(ref _icon, value);
		}

		private ObservableCollection<string> _itemPaths;
		public ObservableCollection<string> ItemPaths
		{
			get => _itemPaths;
			set => SetProperty(ref _itemPaths, value);
		}

		public WorkspaceItem()
		{
			_itemPaths = new ObservableCollection<string>();
		}
	}

	/// <summary>
	/// [NEXTFE VIP ENGINE]
	/// Động cơ quản lý Workspaces (Thư mục ảo Zero-Copy).
	/// Chịu trách nhiệm lưu trữ, nạp và đồng bộ hóa các danh sách file giữa RAM và Ổ cứng.
	/// </summary>
	public class WorkspaceManager : ObservableObject
	{
		// Nơi an toàn để lưu dữ liệu của App mà không bị xóa khi update
		private static readonly string WorkspacesFilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "workspaces.json");

		private ObservableCollection<WorkspaceItem> _workspaces;
		public ObservableCollection<WorkspaceItem> Workspaces
		{
			get => _workspaces;
			private set => SetProperty(ref _workspaces, value);
		}

		public WorkspaceManager()
		{
			Workspaces = new ObservableCollection<WorkspaceItem>();
		}

		/// <summary>
		/// Nạp danh sách Workspace từ ổ cứng khi khởi động App.
		/// </summary>
		public async Task InitializeAsync()
		{
			try
			{
				if (File.Exists(WorkspacesFilePath))
				{
					// [OPTIMIZATION] Dùng luồng phụ để giải mã JSON tránh chặn Splash Screen
					var json = await File.ReadAllTextAsync(WorkspacesFilePath);
					var items = await Task.Run(() => JsonSerializer.Deserialize<ObservableCollection<WorkspaceItem>>(json));

					if (items != null)
					{
						// Quăng mảng đã nạp về UI Thread vì Sidebar sẽ bind trực tiếp vào biến này
						await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
						{
							Workspaces = items;
						});
					}
				}
				else
				{
					// Tạo sẵn một Workspace mẫu nếu người dùng lần đầu mở
					await CreateWorkspaceAsync("Dự án mặc định", "\uE81E"); // Folder icon
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Lỗi khi tải cấu hình Workspaces.");
			}
		}

		/// <summary>
		/// Lưu lại danh sách xuống ổ cứng (Zero-blocking)
		/// </summary>
		public async Task SaveWorkspacesAsync()
		{
			try
			{
				// Tống mọi thao tác serialize nặng nề và I/O ra luồng ngầm
				await Task.Run(async () =>
				{
					var json = JsonSerializer.Serialize(Workspaces, new JsonSerializerOptions { WriteIndented = true });
					await File.WriteAllTextAsync(WorkspacesFilePath, json);
				});
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Lỗi khi lưu cấu hình Workspaces.");
			}
		}

		/// <summary>
		/// Tạo một Workspace mới
		/// </summary>
		public async Task<WorkspaceItem> CreateWorkspaceAsync(string name, string icon = "\uE81E")
		{
			var newWorkspace = new WorkspaceItem
			{
				Id = "workspace://" + Guid.NewGuid().ToString("N"),
				Name = name,
				Icon = icon,
				ItemPaths = new ObservableCollection<string>()
			};

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				Workspaces.Add(newWorkspace);
			});

			await SaveWorkspacesAsync();
			return newWorkspace;
		}

		/// <summary>
		/// Xóa một Workspace
		/// </summary>
		public async Task DeleteWorkspaceAsync(string id)
		{
			var ws = Workspaces.FirstOrDefault(w => w.Id == id);
			if (ws != null)
			{
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					Workspaces.Remove(ws);
				});
				await SaveWorkspacesAsync();
			}
		}

		/// <summary>
		/// Liên kết một File/Folder thực tế vào Workspace (Zero-Copy)
		/// </summary>
		public async Task AddItemToWorkspaceAsync(string workspaceId, string itemPath)
		{
			var ws = Workspaces.FirstOrDefault(w => w.Id == workspaceId);

			// Đảm bảo không trùng lặp đường dẫn
			if (ws != null && !ws.ItemPaths.Contains(itemPath, StringComparer.OrdinalIgnoreCase))
			{
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					ws.ItemPaths.Add(itemPath);
				});
				await SaveWorkspacesAsync();
			}
		}

		/// <summary>
		/// Gỡ một liên kết File/Folder khỏi Workspace
		/// </summary>
		public async Task RemoveItemFromWorkspaceAsync(string workspaceId, string itemPath)
		{
			var ws = Workspaces.FirstOrDefault(w => w.Id == workspaceId);
			if (ws != null)
			{
				var itemToRemove = ws.ItemPaths.FirstOrDefault(p => p.Equals(itemPath, StringComparison.OrdinalIgnoreCase));
				if (itemToRemove != null)
				{
					await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
					{
						ws.ItemPaths.Remove(itemToRemove);
					});
					await SaveWorkspacesAsync();
				}
			}
		}
	}
}