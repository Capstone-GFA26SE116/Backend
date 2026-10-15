using System;
using System.Threading;
using System.Threading.Tasks;
using METANOIA.Application.Dto;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IAiServiceClient
    {
        /// <summary>
        /// Gọi FR-17 POST /learning/task-completed để AI học lại K_bias khi task Done.
        /// Thất bại hoặc lỗi mạng sẽ ghi log và trả về null, không chặn luồng cập nhật task.
        /// </summary>
        Task<AiLearningResponseDto?> RecordTaskCompletionAsync(Guid taskId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gọi FR-10 POST /schedule/propose để AI tính toán slot lịch tối ưu và giải thích.
        /// </summary>
        Task<AiScheduleProposalResponseDto> ProposeScheduleAsync(AiScheduleProposeRequestDto request, CancellationToken cancellationToken = default);
    }
}
