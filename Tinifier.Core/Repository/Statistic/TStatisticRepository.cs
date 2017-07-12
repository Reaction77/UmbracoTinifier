﻿using Tinifier.Core.Models.Db;
using Tinifier.Core.Repository.Common;
using Umbraco.Core;
using Umbraco.Core.Persistence;

namespace Tinifier.Core.Repository.Statistic
{
    public class TStatisticRepository : IEntityCreator<TImageStatistic>, IEntityUpdater<TImageStatistic>, IStatisticRepository<TImageStatistic>
    {
        private readonly UmbracoDatabase _database;

        public TStatisticRepository()
        {
            _database = ApplicationContext.Current.DatabaseContext.Database;
        }

        /// <summary>
        /// Create Statistic
        /// </summary>
        /// <param name="entity">TImageStatistic</param>
        public void Create(TImageStatistic entity)
        {
            _database.Insert(entity);
        }

        /// <summary>
        /// Get Statistic
        /// </summary>
        /// <returns>TImageStatistic</returns>
        public TImageStatistic GetStatistic()
        {
            var query = new Sql("SELECT * FROM TinifierImagesStatistic");

            var statistic = _database.FirstOrDefault<TImageStatistic>(query);

            return statistic;
        }

        /// <summary>
        /// Update Statistic
        /// </summary>
        /// <param name="entity">TImageStatistic</param>
        public void Update(TImageStatistic entity)
        {
            var query = new Sql($"UPDATE TinifierImagesStatistic SET NumberOfOptimizedImages = {entity.NumberOfOptimizedImages}, TotalNumberOfImages = {entity.TotalNumberOfImages}");

            _database.Execute(query);
        }
    }
}