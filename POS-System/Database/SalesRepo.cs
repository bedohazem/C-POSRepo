using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using POS_System.Audit;

namespace POS_System
{
    public static class SalesRepo
    {
        private static SqliteConnection Open()
        {
            return Database.Open();
        }

        private static decimal Round2(decimal x) =>
            Math.Round(x, 2, MidpointRounding.AwayFromZero);

        private static object Money(decimal x) => (double)Round2(x);

        // ===== OLD API (keep signature for compilation; runtime not supported with new schema) =====
        public static long CreateSale(int userId, int branchId, decimal total, decimal paid, decimal change, string method,
            List<(string name, int qty, decimal price)> items)
        {
            // Schema الجديد محتاج VariantId في SaleItems (NOT NULL)
            throw new NotSupportedException("Old CreateSale is not supported. Use CreateSaleV2 (variants).");
        }

        // ===== NEW API =====
        public record SaleLine(
            long VariantId,                 // ✅ NOT NULL (matches SaleItems.VariantId NOT NULL)
            string Name,
            string Barcode,
            string Size,
            string Color,
            int Qty,
            decimal UnitPrice,
            string LineDiscountType,        // None/Percent/Amount
            decimal LineDiscountValue,
            decimal LineTotalAfterDiscount
        );

        public static long CreateSaleV2(
            int userId,
            int branchId,
            string type,                 // "Sale" or "Return"
            long? customerId,
            decimal subTotal,
            string invoiceDiscountType,  // None/Percent/Amount
            decimal invoiceDiscountValue,
            decimal grandTotal,
            decimal paid,
            decimal change,
            string method,
            long? refSaleId,
            string? notes,
            List<SaleLine> items)
        {

            // Determine sign for stock movement:
            // Sale => decrease stock (negative)
            // Return => increase stock (positive)
            var isReturn = string.Equals(type, "Return", StringComparison.OrdinalIgnoreCase);
            var stockSign = isReturn ? +1.0 : -1.0;

            using var con = Open();
            using var tx = con.BeginTransaction();

            try
            {
                long saleId;

                // 1) Insert Sale
                using (var cmd = con.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO Sales(
  AtUtc, UserId, BranchId, CustomerId,
  SubTotal, InvoiceDiscountType, InvoiceDiscountValue, GrandTotal,
  Paid, Change, PaymentMethod,
  Type, RefSaleId, Notes
)
VALUES(
  @t,@u,@b,@cid,
  @sub,@idt,@idv,@gt,
  @paid,@chg,@m,
  @type,@ref,@notes
);
SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@b", branchId);
                    cmd.Parameters.AddWithValue("@cid", (object?)customerId ?? DBNull.Value);

                    cmd.Parameters.AddWithValue("@sub", Money(subTotal));
                    cmd.Parameters.AddWithValue("@idt", string.IsNullOrWhiteSpace(invoiceDiscountType) ? "None" : invoiceDiscountType);
                    cmd.Parameters.AddWithValue("@idv", Money(invoiceDiscountValue));
                    cmd.Parameters.AddWithValue("@gt", Money(grandTotal));

                    cmd.Parameters.AddWithValue("@paid", Money(paid));
                    cmd.Parameters.AddWithValue("@chg", Money(change));
                    cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(method) ? "Cash" : method);

                    cmd.Parameters.AddWithValue("@type", string.IsNullOrWhiteSpace(type) ? "Sale" : type);
                    cmd.Parameters.AddWithValue("@ref", (object?)refSaleId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);

                    saleId = (long)cmd.ExecuteScalar();
                }

                // 2) Insert SaleItems + 3) StockMovements
                foreach (var it in items)
                {
                    if (it.Qty <= 0) continue;

                    decimal unitCost = 0m;

                    // Read CostPrice
                    using (var costCmd = con.CreateCommand())
                    {
                        costCmd.Transaction = tx;
                        costCmd.CommandText = "SELECT IFNULL(CostPrice,0) FROM ProductVariants WHERE Id=@id LIMIT 1;";
                        costCmd.Parameters.AddWithValue("@id", it.VariantId);

                        var costObj = costCmd.ExecuteScalar();
                        if (costObj != null && costObj != DBNull.Value)
                        {
                            var d = Convert.ToDouble(costObj, CultureInfo.InvariantCulture);
                            unitCost = Convert.ToDecimal(d, CultureInfo.InvariantCulture);
                        }
                    }

                    // 2-A) SaleItems
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO SaleItems(
  SaleId, VariantId,
  UnitCost,
  Name, Barcode, Size, Color,
  Qty, UnitPrice,
  LineDiscountType, LineDiscountValue,
  LineTotalAfterDiscount
)
VALUES(
  @sid, @vid,
  @uc,
  @n, @bc, @sz, @cl,
  @q, @up,
  @ldt, @ldv,
  @lta
);";

                        cmd.Parameters.AddWithValue("@sid", saleId);
                        cmd.Parameters.AddWithValue("@vid", it.VariantId);

                        cmd.Parameters.AddWithValue("@uc", Money(unitCost));

                        // NOT NULL columns
                        cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(it.Name) ? "Item" : it.Name);
                        cmd.Parameters.AddWithValue("@bc", it.Barcode ?? "");
                        cmd.Parameters.AddWithValue("@sz", it.Size ?? "");
                        cmd.Parameters.AddWithValue("@cl", it.Color ?? "");

                        cmd.Parameters.AddWithValue("@q", it.Qty);
                        cmd.Parameters.AddWithValue("@up", Money(it.UnitPrice));

                        cmd.Parameters.AddWithValue("@ldt", string.IsNullOrWhiteSpace(it.LineDiscountType) ? "None" : it.LineDiscountType);
                        cmd.Parameters.AddWithValue("@ldv", Money(it.LineDiscountValue));
                        cmd.Parameters.AddWithValue("@lta", Money(it.LineTotalAfterDiscount));

                        cmd.ExecuteNonQuery();

                        if (!isReturn)
                        {
                            var stockNow = StockRepo.GetStock(it.VariantId, branchId);
                            if (stockNow < it.Qty)
                                throw new Exception($"Not enough stock for barcode {it.Barcode}. Available: {stockNow}, Requested: {it.Qty}");
                        }

                        // ✅ Stock movement داخل نفس الـ transaction
                        using (var sm = con.CreateCommand())
                        {
                            sm.Transaction = tx;
                            sm.CommandText = @"
INSERT INTO StockMovements(
    VariantId, Qty, Type, RefId, Notes, AtUtc, UserId, BranchId
)
VALUES(
    @vid, @qty, @type, @ref, @notes, @at, @u, @b
);";

                            sm.Parameters.AddWithValue("@vid", it.VariantId);
                            sm.Parameters.AddWithValue("@qty", isReturn ? it.Qty : -it.Qty);
                            sm.Parameters.AddWithValue("@type", isReturn ? "RETURN" : "SALE");
                            sm.Parameters.AddWithValue("@ref", saleId);
                            sm.Parameters.AddWithValue("@notes", $"Sale #{saleId}");
                            sm.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                            sm.Parameters.AddWithValue("@u", userId);
                            sm.Parameters.AddWithValue("@b", branchId);

                            sm.ExecuteNonQuery();
                        }
                    }

                }


                tx.Commit();
                AuditLog.Write(
                    isReturn ? "RETURN_CREATE" : "SALE_CREATE",
                    $"saleId={saleId}, branchId={branchId}, userId={userId}, customerId={(customerId?.ToString() ?? "null")}, grandTotal={grandTotal}, paid={paid}, method={method}, items={items?.Count ?? 0}"
                );

                return saleId;
            }

            catch (Exception ex)
            {
                try
                {
                    AuditLog.Write(
                        isReturn ? "RETURN_CREATE_FAILED" : "SALE_CREATE_FAILED",
                        $"branchId={branchId}, userId={userId}, customerId={(customerId?.ToString() ?? "null")}, err={ex.Message}"
                    );
                }
                catch { }

                tx.Rollback();
                throw;
            }
        }

        public class SaleHeader
        {
            public long Id { get; set; }
            public string AtUtc { get; set; } = "";
            public int UserId { get; set; }
            public int BranchId { get; set; }
            public long? CustomerId { get; set; }
            public decimal SubTotal { get; set; }
            public string InvoiceDiscountType { get; set; } = "None";
            public decimal InvoiceDiscountValue { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal Paid { get; set; }
            public decimal Change { get; set; }
            public string PaymentMethod { get; set; } = "Cash";
            public string Type { get; set; } = "Sale";
            public long? RefSaleId { get; set; }
            public string? Notes { get; set; }
        }

        public class SaleItemRow
        {
            public long Id { get; set; }
            public long VariantId { get; set; }
            public string Name { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public int Qty { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal UnitCost { get; set; }
            public string LineDiscountType { get; set; } = "None";
            public decimal LineDiscountValue { get; set; }
            public decimal LineTotalAfterDiscount { get; set; }

            // ✅ هنستخدمها في UI
            public int AlreadyReturnedQty { get; set; }
            public int MaxReturnQty => Math.Max(0, Qty - AlreadyReturnedQty);
        }


        public static SaleHeader? GetSaleHeader(long saleId)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id, AtUtc, UserId, BranchId, CustomerId,
       SubTotal, InvoiceDiscountType, InvoiceDiscountValue, GrandTotal,
       Paid, Change, PaymentMethod,
       Type, RefSaleId, Notes
FROM Sales
WHERE Id=@id
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", saleId);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            decimal D(int i) => rd.IsDBNull(i) ? 0m : Convert.ToDecimal(rd.GetDouble(i), CultureInfo.InvariantCulture);

            return new SaleHeader
            {
                Id = rd.GetInt64(0),
                AtUtc = DateTime
                    .Parse(rd.GetString(1))
                    .ToLocalTime()
                    .ToString("dd/MM/yyyy  hh:mm tt"),
                UserId = rd.GetInt32(2),
                BranchId = rd.GetInt32(3),
                CustomerId = rd.IsDBNull(4) ? null : rd.GetInt64(4),

                SubTotal = D(5),
                InvoiceDiscountType = rd.IsDBNull(6) ? "None" : rd.GetString(6),
                InvoiceDiscountValue = D(7),
                GrandTotal = D(8),

                Paid = D(9),
                Change = D(10),
                PaymentMethod = rd.IsDBNull(11) ? "Cash" : rd.GetString(11),

                Type = rd.IsDBNull(12) ? "Sale" : rd.GetString(12),
                RefSaleId = rd.IsDBNull(13) ? null : rd.GetInt64(13),
                Notes = rd.IsDBNull(14) ? null : rd.GetString(14),
            };
        }

        public static List<SaleItemRow> GetSaleItemsWithReturned(long saleId)
        {
            // ✅ AlreadyReturnedQty: مجموع الكميات المرتجعة لنفس الـ VariantId داخل كل مرتجعات مرتبطة بالفاتورة
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  si.Id,
  si.VariantId,
  si.Name,
  IFNULL(si.Barcode,''),
  IFNULL(si.Size,''),
  IFNULL(si.Color,''),
  si.Qty,
  IFNULL(si.UnitPrice,0),
  IFNULL(si.UnitCost,0),
  IFNULL(si.LineDiscountType,'None'),
  IFNULL(si.LineDiscountValue,0),
  IFNULL(si.LineTotalAfterDiscount,0),

  IFNULL((
    SELECT SUM(rsi.Qty)
    FROM Sales rs
    JOIN SaleItems rsi ON rsi.SaleId = rs.Id
    WHERE rs.Type='Return'
      AND rs.RefSaleId = @sid
      AND rsi.VariantId = si.VariantId
  ), 0) AS AlreadyReturnedQty

FROM SaleItems si
WHERE si.SaleId=@sid
ORDER BY si.Id;";
            cmd.Parameters.AddWithValue("@sid", saleId);

            var list = new List<SaleItemRow>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SaleItemRow
                {
                    Id = rd.GetInt64(0),
                    VariantId = rd.GetInt64(1),
                    Name = rd.GetString(2),
                    Barcode = rd.GetString(3),
                    Size = rd.GetString(4),
                    Color = rd.GetString(5),
                    Qty = rd.GetInt32(6),
                    UnitPrice = Convert.ToDecimal(rd.GetDouble(7), CultureInfo.InvariantCulture),
                    UnitCost = Convert.ToDecimal(rd.GetDouble(8), CultureInfo.InvariantCulture),
                    LineDiscountType = rd.GetString(9),
                    LineDiscountValue = Convert.ToDecimal(rd.GetDouble(10), CultureInfo.InvariantCulture),
                    LineTotalAfterDiscount = Convert.ToDecimal(rd.GetDouble(11), CultureInfo.InvariantCulture),
                    AlreadyReturnedQty = Convert.ToInt32(rd.GetDouble(12), CultureInfo.InvariantCulture),
                });
            }
            return list;
        }



        public class SaleListRow
        {
            public long Id { get; set; }
            public string AtUtc { get; set; } = "";
            public string Type { get; set; } = "Sale";
            public string PaymentMethod { get; set; } = "Cash";
            public decimal GrandTotal { get; set; }
            public decimal Paid { get; set; }
            public decimal Change { get; set; }

            public string Username { get; set; } = "";
            public string BranchName { get; set; } = "";
            public string CustomerName { get; set; } = "";

            public string DisplayDate
            {
                get
                {
                    if (DateTime.TryParse(AtUtc, out var dt))
                        return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    return AtUtc;
                }
            }
        }



        public static List<SaleListRow> GetSalesList(
            string? search = null,
            int limit = 200,
            string? type = null)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
SELECT
    s.Id,
    s.AtUtc,
    IFNULL(s.Type, 'Sale'),
    IFNULL(s.PaymentMethod, 'Cash'),
    IFNULL(s.GrandTotal, 0),
    IFNULL(s.Paid, 0),
    IFNULL(s.Change, 0),
    IFNULL(u.Username, ''),
    IFNULL(b.Name, ''),
    IFNULL(c.Name, '')
FROM Sales s
LEFT JOIN Users u ON u.Id = s.UserId
LEFT JOIN Branches b ON b.Id = s.BranchId
LEFT JOIN Customers c ON c.Id = s.CustomerId
WHERE (@type IS NULL OR s.Type = @type)
  AND (
        @q = '' OR
        CAST(s.Id AS TEXT) LIKE @like OR
        IFNULL(u.Username,'') LIKE @like OR
        IFNULL(c.Name,'') LIKE @like OR
        IFNULL(s.PaymentMethod,'') LIKE @like
      )
ORDER BY s.Id DESC
LIMIT @limit;";

            cmd.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@q", (search ?? "").Trim());
            cmd.Parameters.AddWithValue("@like", "%" + ((search ?? "").Trim()) + "%");
            cmd.Parameters.AddWithValue("@limit", limit);

            var list = new List<SaleListRow>();
            using var rd = cmd.ExecuteReader();

            while (rd.Read())
            {
                list.Add(new SaleListRow
                {
                    Id = rd.GetInt64(0),
                    AtUtc = rd.GetString(1),
                    Type = rd.GetString(2),
                    PaymentMethod = rd.GetString(3),
                    GrandTotal = Convert.ToDecimal(rd.GetDouble(4), CultureInfo.InvariantCulture),
                    Paid = Convert.ToDecimal(rd.GetDouble(5), CultureInfo.InvariantCulture),
                    Change = Convert.ToDecimal(rd.GetDouble(6), CultureInfo.InvariantCulture),
                    Username = rd.GetString(7),
                    BranchName = rd.GetString(8),
                    CustomerName = rd.GetString(9),
                });
            }

            return list;
        }
    }
}