﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TecEnergyQuandl.Model.Quandl;

namespace TecEnergyQuandl.Model.ResponseHelpers
{
    public class DatabasesResponse
    {
        public MetaObject Meta { get; set; }
        public List<QuandlDatabase> Databases { get; set; }
    }
}
