using DigitalWorldOnline.Commons.Enums;
using MediatR;

namespace DigitalWorldOnline.Application.Routines.Commands
{
    public class UpdateRoutineExecutionTimeCommand : IRequest
    {
        public RoutineTypeEnum RoutineType { get; }

        public UpdateRoutineExecutionTimeCommand(RoutineTypeEnum routineType)
        {
            RoutineType = routineType;
        }
    }
}