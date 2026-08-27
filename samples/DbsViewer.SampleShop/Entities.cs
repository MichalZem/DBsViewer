namespace DbsViewer.SampleShop;

/// <summary>Zákazník. Nese owned type <see cref="Address"/> a vztah 1:1 na profil.</summary>
public class Customer
{
    public int Id { get; set; }

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Address? BillingAddress { get; set; }

    public CustomerProfile? Profile { get; set; }

    public List<Order> Orders { get; set; } = [];
}

/// <summary>Owned type — mapuje se do stejné tabulky jako <see cref="Customer"/>.</summary>
public class Address
{
    public string Street { get; set; } = "";

    public string City { get; set; } = "";

    public string? PostalCode { get; set; }
}

/// <summary>Vztah 1:1 na zákazníka — primární klíč je zároveň cizí klíč.</summary>
public class CustomerProfile
{
    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public string? Bio { get; set; }

    public string? PreferredLanguage { get; set; }
}

/// <summary>Kategorie se self-reference na nadřazenou kategorii.</summary>
public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? ParentCategoryId { get; set; }

    public Category? ParentCategory { get; set; }

    public List<Category> Children { get; set; } = [];

    public List<Product> Products { get; set; } = [];
}

/// <summary>Produkt. Vztah N:M na <see cref="Tag"/> přes implicitní vazební tabulku.</summary>
public class Product
{
    public int Id { get; set; }

    public string Sku { get; set; } = "";

    public string Name { get; set; } = "";

    public decimal Price { get; set; }

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public int Version { get; set; }

    public List<Tag> Tags { get; set; } = [];

    public List<OrderLine> OrderLines { get; set; } = [];
}

/// <summary>Štítek produktu.</summary>
public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Product> Products { get; set; } = [];
}

/// <summary>Objednávka.</summary>
public class Order
{
    public int Id { get; set; }

    public string Number { get; set; } = "";

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public DateTimeOffset PlacedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];
}

/// <summary>Řádek objednávky — složený primární klíč, tedy identifikující vztah.</summary>
public class OrderLine
{
    public int OrderId { get; set; }

    public int LineNumber { get; set; }

    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>Počítaný sloupec v databázi.</summary>
    public decimal Total { get; private set; }
}

/// <summary>Základ TPH hierarchie plateb.</summary>
public abstract class Payment
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public decimal Amount { get; set; }
}

/// <summary>Platba kartou.</summary>
public class CardPayment : Payment
{
    public string? CardLast4 { get; set; }
}

/// <summary>Platba převodem.</summary>
public class BankTransfer : Payment
{
    public string? Iban { get; set; }
}

/// <summary>Read-only pohled — v modelu mapovaný přes <c>ToView</c>.</summary>
public class OrderSummary
{
    public int OrderId { get; set; }

    public string Number { get; set; } = "";

    public string CustomerEmail { get; set; } = "";

    public decimal Total { get; set; }
}
