// Copyright (c) NQV. Licensed under the MIT License.
// NextFE Studio - Global Performance Wrapper

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Helpers
{
    /// <summary>
    /// Lớp bọc tổng quát (Global Wrapper) điều phối toàn bộ hiệu năng của NextFE.
    /// Thay vì gọi Task.Run hay cấp phát RAM bừa bãi, mọi thứ nên đi qua lõi này.
    /// </summary>
    public static class SpeedEngine
    {
        // 1. GLOBAL THREAD POOL (HẠN CHẾ SPAM CPU)
        // Tạo một bộ lập lịch riêng cho các tác vụ I/O nặng (Đọc file, load icon)
        // Tránh làm nghẽn ThreadPool mặc định của .NET
        private static readonly TaskFactory _ioTaskFactory = new TaskFactory(
            new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, Environment.ProcessorCount).ConcurrentScheduler);

        /// <summary>
        /// Bọc các tác vụ nền (chạy ngầm). Bắn và quên (Fire and Forget) an toàn.
        /// Giúp UI Thread không bao giờ bị dính líu đến các lỗi lặt vặt lúc đọc đĩa.
        /// </summary>
        public static void RunBackground(Action action)
        {
            _ioTaskFactory.StartNew(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Âm thầm ghi log, không bao giờ làm Crash UI app
                    App.Logger.Warn($"[SpeedEngine] Lỗi tác vụ nền: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Bọc tác vụ nền (Async). Ưu tiên cho các thao tác Load I/O dài hạn.
        /// </summary>
        public static void RunBackgroundAsync(Func<Task> action)
        {
            _ioTaskFactory.StartNew(async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    App.Logger.Warn($"[SpeedEngine] Lỗi async tác vụ nền: {ex.Message}");
                }
            });
        }


        // 2. GLOBAL DEBOUNCER (CHỐNG SPAM)
        // Dùng khi người dùng gõ tìm kiếm liên tục, hoặc cuộn trang quá nhanh.
        // Tránh việc app gọi hàm 100 lần trong 1 giây.
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

        /// <summary>
        /// Lớp bọc chống Spam (Debounce). Chỉ thực thi khi đã ngừng gọi một khoảng thời gian.
        /// </summary>
        /// <param name="key">Tên ID của hành động (VD: "SearchFilter")</param>
        /// <param name="action">Hành động cần làm</param>
        /// <param name="delayMs">Độ trễ (Thường là 150-300ms)</param>
        public static void Debounce(string key, Action action, int delayMs = 200)
        {
            // Hủy tác vụ cũ nếu người dùng vẫn đang thao tác
            if (_debounceTokens.TryGetValue(key, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var newCts = new CancellationTokenSource();
            _debounceTokens[key] = newCts;

            Task.Delay(delayMs, newCts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    action();
                    _debounceTokens.TryRemove(key, out _);
                }
            }, TaskScheduler.Default);
        }


        // 3. GLOBAL MEMORY POOL (ZERO-ALLOCATION WRAPPER)
        // Thuê mảng RAM có sẵn thay vì bắt Windows tạo mới liên tục.

        /// <summary>
        /// Thuê một mảng (Array) kích thước cố định từ Pool hệ thống. 
        /// Nhanh gấp 10 lần so với việc dùng "new T[size]".
        /// Phù hợp để load đệm danh sách File trung gian.
        /// </summary>
        public static T[] RentArray<T>(int minLength)
        {
            return ArrayPool<T>.Shared.Rent(minLength);
        }

        /// <summary>
        /// Trả mảng lại cho hệ thống sau khi dùng xong (Bắt buộc phải gọi sau khi Rent).
        /// </summary>
        public static void ReturnArray<T>(T[] array, bool clearArray = false)
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, clearArray);
            }
        }
    }
}