﻿using Tinifier.Core.Infrastructure.Enums;
using Tinifier.Core.Models.Db;
using Tinifier.Core.Repository.Repository;
using Tinifier.Core.Services.Interfaces;

namespace Tinifier.Core.Services.Services
{
    public class StateService : IStateService
    {
        private readonly TStateRepository _stateRepository;

        public StateService()
        {
            _stateRepository = new TStateRepository();
        }

        public void CreateState(int numberOfImages)
        {
            var state = new TState
            {
                CurrentImage = 0,
                AmounthOfImages = numberOfImages,
                StatusType = Statuses.InProgress
            };

            _stateRepository.Create(state);
        }

        public TState GetState()
        {
            var state = _stateRepository.GetByKey((int)Statuses.InProgress);

            return state;
        }

        public void UpdateState()
        {
            var state = _stateRepository.GetByKey((int)Statuses.InProgress);

            if(state.CurrentImage < state.AmounthOfImages)
            {
                state.CurrentImage++;
            }

            if (state.CurrentImage == state.AmounthOfImages)
            {
                state.StatusType = Statuses.Done;
            }

            _stateRepository.Update(state);
        }
    }
}
