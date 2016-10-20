﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TecEnergyQuandl.Model.Quandl;
using TecEnergyQuandl.Model.ResponseHelpers;

namespace TecEnergyQuandl
{
    public static class FetchData
    {
        private static List<QuandlDatasetGroup> datasetsGroups;
        private static List<QuandlDatasetGroup> datasetsDataGroups = new List<QuandlDatasetGroup>();

        private static int datasetsFetched = 0;
        public static async Task BeginDownloadData()
        {
            // Download first page and check meta
            Console.WriteLine("Fetching datasets\n---------------------------------------");
            datasetsGroups = PostgresHelpers.QuandlDatasetActions.GetImportedDatasets();

            Console.WriteLine("\nSelected datasets models - quantity:");
            datasetsGroups.ForEach(d =>
                Console.WriteLine(" -[DB Model] " + d.DatabaseCode + " - " + d.Datasets.Count)
            );
            Console.WriteLine();

            int count = 0;
            foreach (QuandlDatasetGroup datasetGroup in datasetsGroups)
            {
                // Update groups to fetched count
                count++;

                // Identify current group
                Utils.ConsoleInformer.InformSimple("Group model: [" + datasetGroup.DatabaseCode + "]. Group:" + count + "/" + datasetsGroups.Count);

                // Request all datasets from group
                await DownloadDatasetsDataAsync(datasetGroup, datasetGroup.Datasets.Count);
            }

            Console.WriteLine("");

            // Make datasets model tables
            PostgresHelpers.QuandlDatasetActions.InsertQuandlDatasetsData(datasetsGroups);
        }

        private static async Task DownloadDatasetsDataAsync(QuandlDatasetGroup datasetGroup, int to)
        {
            await Task.WhenAll(datasetGroup.Datasets.Select(d => DownloadDatasetDataAsync(d, to)));
            Console.WriteLine();

            // Reset datasets fetched count for next group
            datasetsFetched = 0;
        }

        private static async Task DownloadDatasetDataAsync(QuandlDataset dataset, int to)
        {
            using (WebClient client = new WebClient())
            {
                string data = await client.DownloadStringTaskAsync(new Uri("https://www.quandl.com/api/v3/datasets/" + dataset.DatabaseCode + "/" + dataset.DatasetCode + "/data.json?api_key=" + Utils.Constants.API_KEY));
                DataResponse response =
                        JsonConvert.DeserializeObject<DataResponse>(data, new JsonSerializerSettings { ContractResolver = Utils.Converters.MakeUnderscoreContract() });

                QuandlDatasetData datasetData = response.DatasetData;
                datasetData.SetBaseDataset(dataset);

                datasetsFetched++;
                Utils.ConsoleInformer.PrintProgress("1C", "Fetching dataset [" + dataset.DatasetCode + "]: ", Utils.Helpers.GetPercent(datasetsFetched, to).ToString() + "%");

                // Replace old uncomplete dataset with new one
                ReplaceCompleteDataset(datasetData);
            }
        }

        private static void ReplaceCompleteDataset(QuandlDatasetData datasetData)
        {
            // Reference current groups list
            var currentDatasetGroup = datasetsGroups
                    .Find(dg => dg.DatabaseCode == datasetData.DatabaseCode)
                    .Datasets;
            
            // Index of the dataset to update
            var index = currentDatasetGroup.IndexOf(currentDatasetGroup.First(ds => ds.Id == datasetData.Id));

            // Replace
            if (index != -1)
                currentDatasetGroup[index] = datasetData;
        }
    }
}