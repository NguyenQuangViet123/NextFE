// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Windows.Input;

// [OPTIMIZATION] Bổ sung System.Collections.Generic để sử dụng List cục bộ
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class StatusBarViewModel : ObservableObject
	{
		// ====================================================================================
		// [VIP OPTIMIZATION] LAZY DI EVALUATION
		// ====================================================================================
		// Không cấp phát Service ngay lập tức lúc khởi tạo Tab. Trì hoãn đến khi thực sự cần.
		private IContentPageContext? _contentPageContext;
		private IContentPageContext ContentPageContext => _contentPageContext ??= Ioc.Default.GetRequiredService<IContentPageContext>();

		private IDevToolsSettingsService? _devToolsSettingsService;
		private IDevToolsSettingsService DevToolsSettingsService => _devToolsSettingsService ??= Ioc.Default.GetRequiredService<IDevToolsSettingsService>();

		// The first branch will always be the active one.
		public const int ACTIVE_BRANCH_INDEX = 0;

		private string? _gitRepositoryPath;

		private readonly ObservableCollection<BranchItem> _localBranches = [];

		private readonly ObservableCollection<BranchItem> _remoteBranches = [];

		public bool IsBranchesFlyoutExpanded { get; set; } = false;

		private string? _DirectoryItemCount;
		public string? DirectoryItemCount
		{
			get => _DirectoryItemCount;
			set => SetProperty(ref _DirectoryItemCount, value);
		}

		private string? _GitBranchDisplayName;
		public string? GitBranchDisplayName
		{
			get => _GitBranchDisplayName;
			private set => SetProperty(ref _GitBranchDisplayName, value);
		}

		private int _SelectedBranchIndex;
		public int SelectedBranchIndex
		{
			get => _SelectedBranchIndex;
			set
			{
				if (SetProperty(ref _SelectedBranchIndex, value) &&
					value != -1 &&
					(value != ACTIVE_BRANCH_INDEX || !_ShowLocals) &&
					value < Branches.Count)
				{
					CheckoutRequested?.Invoke(this, Branches[value].Name);
				}
			}
		}

		private bool _ShowLocals = true;
		public bool ShowLocals
		{
			get => _ShowLocals;
			set
			{
				if (SetProperty(ref _ShowLocals, value))
				{
					OnPropertyChanged(nameof(Branches));

					if (value)
						SelectedBranchIndex = ACTIVE_BRANCH_INDEX;
				}
			}
		}

		private string _StatusInfo = "0 / 0";
		public string StatusInfo
		{
			get => _StatusInfo;
			set => SetProperty(ref _StatusInfo, value);
		}

		private string _ExtendedStatusInfo = string.Format("CommitsNumber".GetLocalizedResource(), 0);
		public string ExtendedStatusInfo
		{
			get => _ExtendedStatusInfo;
			set => SetProperty(ref _ExtendedStatusInfo, value);
		}

		public bool ShowOpenInIDEButton
		{
			get
			{
				return DevToolsSettingsService.OpenInIDEOption == OpenInIDEOption.AllLocations ||
					   (DevToolsSettingsService.OpenInIDEOption == OpenInIDEOption.GitRepos && GitBranchDisplayName is not null);
			}
		}

		public ObservableCollection<BranchItem> Branches => _ShowLocals
			? _localBranches
			: _remoteBranches;

		public EventHandler<string>? CheckoutRequested;

		public ICommand NewBranchCommand { get; }

		// Các biến Tracking để Zero-allocation khi không đổi trạng thái
		private int _lastAhead = -1;
		private int _lastBehind = -1;

		public StatusBarViewModel()
		{
			NewBranchCommand = new AsyncRelayCommand(()
				=> GitHelpers.CreateNewBranchAsync(_gitRepositoryPath!, _localBranches[ACTIVE_BRANCH_INDEX].Name));

			DevToolsSettingsService.PropertyChanged += (s, e) =>
			{
				// [OPTIMIZATION] Bỏ khối switch cồng kềnh, dùng if cho 1 trường hợp duy nhất để xử lý nhanh
				if (e.PropertyName == nameof(DevToolsSettingsService.OpenInIDEOption))
				{
					OnPropertyChanged(nameof(ShowOpenInIDEButton));
				}
			};
		}

		public void UpdateGitInfo(bool isGitRepository, string? repositoryPath, BranchItem? head)
		{
			// [OPTIMIZATION] Truy xuất an toàn và ngắn gọn hơn cho cờ SearchResults
			bool isSearchResults = ContentPageContext.ShellPage?.InstanceViewModel?.IsPageTypeSearchResults ?? false;

			GitBranchDisplayName = isGitRepository && head is not null && !isSearchResults ? head.Name : null;

			_gitRepositoryPath = repositoryPath;

			// Change ShowLocals value only if branches flyout is closed
			if (!IsBranchesFlyoutExpanded)
				ShowLocals = true;

			var behind = head?.BehindBy ?? 0;
			var ahead = head?.AheadBy ?? 0;

			// ====================================================================================
			// [VIP OPTIMIZATION] ZERO STRING ALLOCATION
			// ====================================================================================
			// Chỉ cấp phát bộ nhớ để nối chuỗi (String Interpolation & Format) khi thông số thay đổi.
			// Giúp FPS không bị giật khi lướt qua hàng trăm file trong thư mục Git.
			if (_lastAhead != ahead || _lastBehind != behind)
			{
				_lastAhead = ahead;
				_lastBehind = behind;
				ExtendedStatusInfo = string.Format(Strings.GitSyncStatusExtendedInfo.GetLocalizedResource(), ahead, behind);
				StatusInfo = $"{ahead} / {behind}";
			}

			OnPropertyChanged(nameof(ShowOpenInIDEButton));
		}

		public async Task LoadBranches()
		{
			if (string.IsNullOrEmpty(_gitRepositoryPath))
				return;

			var branches = await GitHelpers.GetBranchesNames(_gitRepositoryPath);

			// ====================================================================================
			// [OPTIMIZATION] OFFLOAD DATA SORTING (GIẢM TẢI COLLECTION CHANGED)
			// ====================================================================================
			// Xử lý phân loại trên RAM bằng List cục bộ trước, KHÔNG đẩy trực tiếp vào ObservableCollection
			// ngay trong vòng lặp để UI Thread không bị bóp nghẹt bởi hàng trăm event render.
			var localTemp = new List<BranchItem>();
			var remoteTemp = new List<BranchItem>();

			foreach (var branch in branches)
			{
				if (branch.IsRemote)
					remoteTemp.Add(branch);
				else
					localTemp.Add(branch);
			}

			// Cập nhật lên UI Collection gọn gàng
			_localBranches.Clear();
			foreach (var branch in localTemp)
			{
				_localBranches.Add(branch);
			}

			_remoteBranches.Clear();
			foreach (var branch in remoteTemp)
			{
				_remoteBranches.Add(branch);
			}

			SelectedBranchIndex = ShowLocals ? ACTIVE_BRANCH_INDEX : -1;
		}

		public Task ExecuteDeleteBranch(string? branchName)
		{
			return GitHelpers.DeleteBranchAsync(_gitRepositoryPath, GitBranchDisplayName, branchName);
		}
	}
}