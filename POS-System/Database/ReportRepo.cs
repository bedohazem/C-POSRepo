using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using POS_System.Audit;

namespace POS_System
{
    public static class ReportRepo
    {
        private static SqliteConnection Open()
        {
            var con = new SqliteConnection(Database.ConnStr);
            con.Open();
            using var fk = con.CreateCommand();
            fk.CommandText = "PRAGMA foreign_keys = ON;";
            fk.ExecuteNonQuery();
            return con;
        }

        private static (string fromUtc, string toUtc) ToUtcRange(DateTime fromLocal, DateTime toLocalInclusive)
        {
            // نخليها local range -> utc ISO
            var startLocal = new DateTime(fromLocal.Year, fromLocal.Month, fromLocal.Day, 0, 0, 0, DateTimeKind.Local);
            var endLocal = new DateTime(toLocalInclusive.Year, toLocalInclusive.Month, toLocalInclusive.Day, 23, 59, 59, DateTimeKind.Local);

            return (startLocal.ToUniversalTime().ToString("o"), endLocal.ToUniversalTime().ToString("o"));
        }

        public class SalesSummary
        {
            public int Invoices { get; set; }
            public decimal NetSales { get; set; }     // GrandTotal sum
            public decimal SubTotal { get; set; }
            public decimal Discounts { get; set; }    // invoice discounts sum
            public decimal Paid { get; set; }
            public decimal Returns { get; set; }      // GrandTotal for returns
        }

        public class SalesByPeriodRow
        {
            public string Period { get; set; } = "";
            public int Invoices { get; set; }
            public decimal NetSales { get; set; }
        }

        public class PaymentRow
        {
            public string Method { get; set; } = "";
            public int Invoices { get; set; }
            public decimal Total { get; set; }
        }

        public class ProductRankRow
        {
            public string ProductName { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public int Qty { get; set; }
            public decimal Revenue { get; set; }
            public decimal Profit { get; set; }
        }

        public class CashierPerfRow
        {
            public string Username { get; set; } = "";
            public int SalesInvoices { get; set; }
            public decimal SalesTotal { get; set; }
            public int ReturnInvoices { get; set; }
            public decimal ReturnTotal { get; set; }
            public decimal Net { get; set; }
            public decimal AvgInvoice { get; set; }
        }

        public class ProfitByPeriodRow
        {
            public string Period { get; set; } = "";
            public decimal Profit { get; set; }
        }

        // ===============================
        // ========== Queries ============
        // ===============================

        public static SalesSummary GetSalesSummary(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  IFNULL(SUM(CASE WHEN Type='Sale' THEN 1 ELSE 0 END), 0) AS SaleInvoices,
  IFNULL(SUM(CASE WHEN Type='Sale' THEN IFNULL(GrandTotal,0) ELSE 0 END), 0) AS SalesNet,
  IFNULL(SUM(CASE WHEN Type='Sale' THEN IFNULL(SubTotal,0) ELSE 0 END), 0) AS SalesSub,
  IFNULL(SUM(CASE WHEN Type='Sale' THEN IFNULL(InvoiceDiscountValue,0) ELSE 0 END), 0) AS SalesDisc,
  IFNULL(SUM(CASE WHEN Type='Sale' THEN IFNULL(Paid,0) ELSE 0 END), 0) AS SalesPaid,
  IFNULL(SUM(CASE WHEN Type='Return' THEN IFNULL(GrandTotal,0) ELSE 0 END), 0) AS ReturnsNet
FROM Sales
WHERE AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR BranchId=@bid);";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            rd.Read();

            decimal D(int i) => rd.IsDBNull(i) ? 0m : Convert.ToDecimal(rd.GetValue(i));
            int I(int i) => rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));

            return new SalesSummary
            {
                Invoices = I(0),
                NetSales = D(1),
                SubTotal = D(2),
                Discounts = D(3),
                Paid = D(4),
                Returns = D(5),
            };
        }

        public static List<SalesByPeriodRow> GetSalesByDay(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<SalesByPeriodRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  substr(AtUtc,1,10) AS Day,
  COUNT(*) AS Invoices,
  SUM(IFNULL(GrandTotal,0)) AS NetSales
FROM Sales
WHERE Type='Sale'
  AND AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR BranchId=@bid)
GROUP BY substr(AtUtc,1,10)
ORDER BY Day;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SalesByPeriodRow
                {
                    Period = rd.GetString(0),
                    Invoices = Convert.ToInt32(rd.GetInt64(1)),
                    NetSales = Convert.ToDecimal(rd.GetDouble(2))
                });
            }
            return list;
        }

        public static List<SalesByPeriodRow> GetSalesByMonth(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<SalesByPeriodRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  substr(AtUtc,1,7) AS Month,
  COUNT(*) AS Invoices,
  SUM(IFNULL(GrandTotal,0)) AS NetSales
FROM Sales
WHERE Type='Sale'
  AND AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR BranchId=@bid)
GROUP BY substr(AtUtc,1,7)
ORDER BY Month;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SalesByPeriodRow
                {
                    Period = rd.GetString(0),
                    Invoices = Convert.ToInt32(rd.GetInt64(1)),
                    NetSales = Convert.ToDecimal(rd.GetDouble(2))
                });
            }
            return list;
        }

        public static List<PaymentRow> GetPaymentMethods(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<PaymentRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  IFNULL(PaymentMethod,'Cash') AS Method,
  COUNT(*) AS Invoices,
  SUM(IFNULL(GrandTotal,0)) AS Total
FROM Sales
WHERE Type='Sale'
  AND AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR BranchId=@bid)
GROUP BY IFNULL(PaymentMethod,'Cash')
ORDER BY Total DESC;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new PaymentRow
                {
                    Method = rd.GetString(0),
                    Invoices = Convert.ToInt32(rd.GetInt64(1)),
                    Total = Convert.ToDecimal(rd.GetDouble(2))
                });
            }
            return list;
        }

        public static List<ProductRankRow> GetTopProducts(DateTime fromLocal, DateTime toLocal, int topN, int? branchId = null, bool ascending = false)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<ProductRankRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = $@"
SELECT
  si.Name,
  IFNULL(si.Barcode,'') AS Barcode,
  IFNULL(si.Size,'') AS Size,
  IFNULL(si.Color,'') AS Color,
  IFNULL(SUM(si.Qty),0) AS Qty,
  IFNULL(SUM(si.LineTotalAfterDiscount),0) AS Revenue,
  IFNULL(SUM((IFNULL(si.UnitPrice,0) - IFNULL(si.UnitCost,0)) * si.Qty),0) AS Profit
FROM SaleItems si
JOIN Sales s ON s.Id = si.SaleId
WHERE s.Type='Sale'
  AND s.AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR s.BranchId=@bid)
GROUP BY si.Name, IFNULL(si.Barcode,''), IFNULL(si.Size,''), IFNULL(si.Color,'')
ORDER BY Qty {(ascending ? "ASC" : "DESC")}
LIMIT @n;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", topN);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new ProductRankRow
                {
                    ProductName = rd.GetString(0),
                    Barcode = rd.GetString(1),
                    Size = rd.GetString(2),
                    Color = rd.GetString(3),
                    Qty = Convert.ToInt32(rd.GetDouble(4)),
                    Revenue = Convert.ToDecimal(rd.GetDouble(5)),
                    Profit = Convert.ToDecimal(rd.GetDouble(6))
                });
            }
            return list;
        }

        public static List<CashierPerfRow> GetCashierPerformance(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<CashierPerfRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  u.Username,
  SUM(CASE WHEN s.Type='Sale' THEN 1 ELSE 0 END) AS SalesInvoices,
  SUM(CASE WHEN s.Type='Sale' THEN IFNULL(s.GrandTotal,0) ELSE 0 END) AS SalesTotal,
  SUM(CASE WHEN s.Type='Return' THEN 1 ELSE 0 END) AS ReturnInvoices,
  SUM(CASE WHEN s.Type='Return' THEN IFNULL(s.GrandTotal,0) ELSE 0 END) AS ReturnTotal
FROM Sales s
JOIN Users u ON u.Id = s.UserId
WHERE s.AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR s.BranchId=@bid)
GROUP BY u.Username
ORDER BY SalesTotal DESC;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var salesInv = Convert.ToInt32(rd.GetDouble(1));
                var salesTot = Convert.ToDecimal(rd.GetDouble(2));
                var retInv = Convert.ToInt32(rd.GetDouble(3));
                var retTot = Convert.ToDecimal(rd.GetDouble(4));
                var net = salesTot - retTot;
                var avg = salesInv == 0 ? 0 : (salesTot / salesInv);

                list.Add(new CashierPerfRow
                {
                    Username = rd.GetString(0),
                    SalesInvoices = salesInv,
                    SalesTotal = salesTot,
                    ReturnInvoices = retInv,
                    ReturnTotal = retTot,
                    Net = net,
                    AvgInvoice = avg
                });
            }
            return list;
        }

        public static List<ProfitByPeriodRow> GetProfitByDay(DateTime fromLocal, DateTime toLocal, int? branchId = null)
        {
            var (fromUtc, toUtc) = ToUtcRange(fromLocal, toLocal);

            var list = new List<ProfitByPeriodRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  substr(s.AtUtc,1,10) AS Day,
  IFNULL(SUM((IFNULL(si.UnitPrice,0) - IFNULL(si.UnitCost,0)) * si.Qty),0) AS Profit
FROM SaleItems si
JOIN Sales s ON s.Id = si.SaleId
WHERE s.Type='Sale'
  AND s.AtUtc BETWEEN @from AND @to
  AND (@bid IS NULL OR s.BranchId=@bid)
GROUP BY substr(s.AtUtc,1,10)
ORDER BY Day;";

            cmd.Parameters.AddWithValue("@from", fromUtc);
            cmd.Parameters.AddWithValue("@to", toUtc);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new ProfitByPeriodRow
                {
                    Period = rd.GetString(0),
                    Profit = Convert.ToDecimal(rd.GetDouble(1))
                });
            }
            return list;
        }
    }
}