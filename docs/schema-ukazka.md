# Schéma databáze main

Provider: Sqlite  
Vygenerováno: 2026-08-28 04:20 UTC  
Tabulek: 10, vazeb: 8

## Categories

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| Name | `TEXT` | ne |  |  |
| ParentCategoryId | `INTEGER` | ano | FK |  |

**Indexy**

- `IX_Categories_ParentCategoryId` (ParentCategoryId)

**Cizí klíče**

- `FK_Categories_Categories_ParentCategoryId`: (ParentCategoryId) → Categories (Id), onDelete Restrict

## CustomerProfiles

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| CustomerId | `INTEGER` | ne | PK, FK |  |
| Bio | `TEXT` | ano |  |  |
| PreferredLanguage | `TEXT` | ano |  |  |

**Cizí klíče**

- `FK_CustomerProfiles_Customers_CustomerId`: (CustomerId) → Customers (Id), onDelete Cascade

## Customers

> Zákazníci e-shopu

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| BillingCity | `TEXT` | ano |  |  |
| BillingPostalCode | `TEXT` | ano |  |  |
| BillingStreet | `TEXT` | ano |  |  |
| CreatedAt | `TEXT` | ne |  | default `CURRENT_TIMESTAMP` |
| DisplayName | `TEXT` | ano |  |  |
| Email | `TEXT` | ne |  |  |

**Indexy**

- UNIQUE `UX_Customers_Email` (Email)

## OrderLines

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| OrderId | `INTEGER` | ne | PK, FK |  |
| LineNumber | `INTEGER` | ne | PK |  |
| ProductId | `INTEGER` | ne | FK |  |
| Quantity | `INTEGER` | ne |  |  |
| Total | `TEXT` | ne |  | computed |
| UnitPrice | `TEXT` | ne |  |  |

**Indexy**

- `IX_OrderLines_ProductId` (ProductId)

**Cizí klíče**

- `FK_OrderLines_Orders_OrderId`: (OrderId) → Orders (Id), onDelete Cascade
- `FK_OrderLines_Products_ProductId`: (ProductId) → Products (Id), onDelete Restrict

## Orders

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| CustomerId | `INTEGER` | ne | FK |  |
| Number | `TEXT` | ne |  |  |
| PlacedAt | `TEXT` | ne |  |  |

**Indexy**

- `IX_Orders_Customer_PlacedAt` (CustomerId, PlacedAt)
- UNIQUE `UX_Orders_Number` (Number)

**Cizí klíče**

- `FK_Orders_Customers_CustomerId`: (CustomerId) → Customers (Id), onDelete Restrict

## OrderSummaries

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| CustomerEmail | `TEXT` | ne |  |  |
| Number | `TEXT` | ne |  |  |
| OrderId | `INTEGER` | ne |  |  |
| Total | `TEXT` | ne |  |  |

## Payments

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| Amount | `TEXT` | ne |  |  |
| CardLast4 | `TEXT` | ano |  |  |
| Iban | `TEXT` | ano |  |  |
| OrderId | `INTEGER` | ne | FK |  |
| PaymentType | `TEXT` | ne |  |  |

**Indexy**

- `IX_Payments_OrderId` (OrderId)

**Cizí klíče**

- `FK_Payments_Orders_OrderId`: (OrderId) → Orders (Id), onDelete Cascade

## Products

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| CategoryId | `INTEGER` | ne | FK |  |
| Name | `TEXT` | ne |  | Obchodní název produktu |
| Price | `TEXT` | ne |  |  |
| Sku | `TEXT` | ne |  |  |
| Version | `INTEGER` | ne |  |  |

**Indexy**

- `IX_Products_Category_Name` (CategoryId, Name)
- UNIQUE `UX_Products_Sku` (Sku)

**Cizí klíče**

- `FK_Products_Categories_CategoryId`: (CategoryId) → Categories (Id), onDelete Restrict

## ProductTags

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| ProductsId | `INTEGER` | ne | PK, FK |  |
| TagsId | `INTEGER` | ne | PK, FK |  |

**Indexy**

- `IX_ProductTags_TagsId` (TagsId)

**Cizí klíče**

- `FK_ProductTags_Products_ProductsId`: (ProductsId) → Products (Id), onDelete Cascade
- `FK_ProductTags_Tags_TagsId`: (TagsId) → Tags (Id), onDelete Cascade

## Tags

| Sloupec | Typ | Null | Klíč | Poznámka |
|---|---|---|---|---|
| Id | `INTEGER` | ne | PK | identity |
| Name | `TEXT` | ne |  |  |

**Indexy**

- UNIQUE `UX_Tags_Name` (Name)

