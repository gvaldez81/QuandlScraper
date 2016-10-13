﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TecEnergyQuandl.Model
{
    public class QuandlDatabase
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string DatabaseCode { get; set; }
        public string Description { get; set; }
        public long DatasetsCount { get; set; }
        public long Downloads { get; set; }
        public bool Premium { get; set; }
        public string Image { get; set; }
        public bool Favorite { get; set; }
    }
}