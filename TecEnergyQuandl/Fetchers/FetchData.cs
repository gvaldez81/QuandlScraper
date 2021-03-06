﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TecEnergyQuandl.Model.Quandl;
using TecEnergyQuandl.Model.ResponseHelpers;
using TecEnergyQuandl.PostgresHelpers;
using TecEnergyQuandl.Utils;

namespace TecEnergyQuandl
{
    public static class FetchData
    {
        private static List<QuandlDatasetGroup> datasetsGroups;
        private static List<QuandlDatasetDataGroup> datasetsDataGroups = new List<QuandlDatasetDataGroup>();
        private static List<Tuple<string, string>> errors = new List<Tuple<string, string>>();
        private static List<QuandlDatasetGroup> failedToFetch = new List<QuandlDatasetGroup>();

        private static int datasetsFetched = 0;
        private static bool blocked = false;

        public static async Task BeginDownloadData()
        {
            // Download first page and check meta
            Console.WriteLine("Fetching datasets\n---------------------------------------");
            datasetsGroups = PostgresHelpers.QuandlDatasetActions.GetImportedDatasets();
            await StartFetching();
        }

        private static async Task StartFetching()
        {
            Console.WriteLine("\nSelected datasets models - quantity:");
            datasetsGroups.ForEach(d =>
                Console.WriteLine(" -[DB Model] " + d.DatabaseCode + " - " + d.Datasets.Count)
            );
            Console.WriteLine();

            Console.WriteLine("\nDetecting newest data available:");
            foreach (QuandlDatasetGroup datasetGroup in datasetsGroups)
            {
                List<Tuple<DateTime, string>> datasetNewestDateList = PostgresHelpers.QuandlDatasetActions.GetNewestImportedData(datasetGroup);

                // Item1 = Newest date of data
                // Item2 = Dataset code
                foreach (var tuple in datasetNewestDateList)
                {
                    // Will only add those who dataset is imported
                    QuandlDataset dataset = datasetGroup.Datasets.Find(d => d.DatasetCode == tuple.Item2);
                    if (dataset != null) { dataset.LastFetch = tuple.Item1; }
                }
            }

            int count = 0;
            foreach (QuandlDatasetGroup datasetGroup in datasetsGroups)
            {
                // Update groups to fetched count
                count++;

                // Identify current group
                Utils.ConsoleInformer.InformSimple("Group model: [" + datasetGroup.DatabaseCode + "]. Group:" + count + "/" + datasetsGroups.Count);

                // Make datasets model tables
                Console.WriteLine("Creating unique table model for datasets:");
                SchemaActions.CreateQuandlDatasetDataTable(datasetGroup);
                Console.WriteLine();

                // Request all datasets from group
                await DownloadDatasetsDataAsync(datasetGroup, datasetGroup.Datasets.Count);
            }

            if (failedToFetch.Any())
            {
                datasetsGroups.Clear();
                datasetsGroups.AddRange(failedToFetch);

                Console.WriteLine("\n######################################################################");
                Console.WriteLine("\nFetching failed datasets data");
                Console.WriteLine("Waiting 11 minutes (quandl limitation) before fetching remaning ones");

                for (int totalSeconds = 11 * 60; totalSeconds >= 0; totalSeconds--)
                {
                    int seconds = totalSeconds % 60;
                    int minutes = totalSeconds / 60;
                    string time = minutes + ":" + seconds;

                    Console.CursorLeft = 0;
                    Console.Write("{0} ", time);    // Add space to make sure to override previous contents
                    System.Threading.Thread.Sleep(1000);
                }

                failedToFetch.Clear();
                await StartFetching();
            }

            // Make datasets model tables
            //PostgresHelpers.QuandlDatasetActions.InsertQuandlDatasetsData(datasetsDataGroups);
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
            using (PatientWebClient client = new PatientWebClient())
            {
                try
                {
                    string data = await client.DownloadStringTaskAsync(new Uri("https://www.quandl.com/api/v3/datasets/" + dataset.DatabaseCode +
                                                                            "/" + dataset.DatasetCode + "/data.json?api_key=" + Utils.Constants.API_KEY +
                                                                            "&start_date=" + dataset.LastFetch.GetValueOrDefault(DateTime.MinValue).AddDays(1).ToString("yyyy-MM-dd"))); // Add one day because I dont want to include the current newest in the json

                    DataResponse response =
                            JsonConvert.DeserializeObject<DataResponse>(data, new JsonSerializerSettings { ContractResolver = Utils.Converters.MakeUnderscoreContract() });

                    QuandlDatasetData datasetData = response.DatasetData;
                    datasetData.SetBaseDataset(dataset);

                    datasetsFetched++;

                    Utils.ConsoleInformer.PrintProgress("1C", "Fetching dataset [" + dataset.DatasetCode + "]: ", Utils.Helpers.GetPercent(datasetsFetched, to).ToString() + "%");

                    // Replace old uncomplete dataset with new one
                    //ReplaceCompleteDataset(datasetData);
                    //AddCompleteDataset(datasetData);

                    // Insert
                    QuandlDatasetDataGroup datasetGroup = new QuandlDatasetDataGroup() { DatabaseCode = datasetData.DatabaseCode, Datasets = new List<QuandlDatasetData>() };
                    datasetGroup.Datasets.Add(datasetData);
                    PostgresHelpers.QuandlDatasetActions.InsertQuandlDatasetsDataGroup(datasetGroup);
                }
                catch (Exception e)
                {
                    // Add to fetch later (Create datasetgroup if doesnt exists in failed list)
                    if (!failedToFetch.Exists(d => d.DatabaseCode == dataset.DatabaseCode))
                        failedToFetch.Add(new QuandlDatasetGroup() { DatabaseCode = dataset.DatabaseCode, Datasets = new List<QuandlDataset>() });

                    failedToFetch.Find(d => d.DatabaseCode == dataset.DatabaseCode).Datasets.Add(dataset);

                    if (e.Message.Contains("(429)"))
                    {
                        // Print only once
                        if (!blocked)
                            Utils.ConsoleInformer.Inform("Looks like quandl just blocked you");

                        blocked = true;
                    }

                    // Log
                    Utils.Helpers.Log("Failed to fetch data: from dataset: [" + dataset.DatabaseCode + "/" + dataset.DatasetCode + "] Will try to recover", "Ex: " + e.Message);
                    //errors.Add(new Tuple<string, string>("Failed to fetch data: from dataset: [" + dataset.DatabaseCode + "/" + dataset.DatasetCode + "]", "Ex: " + e.Message));
                }
            }
        }

        private static void AddCompleteDataset(QuandlDatasetData datasetData)
        {
            // If group doesnt exists, create it
            if (!datasetsDataGroups.Exists(d => d.DatabaseCode == datasetData.DatabaseCode))
                datasetsDataGroups.Add(new QuandlDatasetDataGroup() { DatabaseCode = datasetData.DatabaseCode, Datasets = new List<QuandlDatasetData>() });

            // Add it
            datasetsDataGroups.Find(d => d.DatabaseCode == datasetData.DatabaseCode).Datasets.Add(datasetData);
        }

        /**
         * Not used anymore
         */
         [Obsolete("Use AddCompleteDataset instead")]
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
