using EcommercePortfolio.Domain.Enums;

namespace EcommercePortfolio.Domain.Extensions;

public static class StatusExtensions
{
    public static string ToDbString(this OrderStatus s) => s switch
    {
        OrderStatus.Pending    => "pending",
        OrderStatus.Paid       => "paid",
        OrderStatus.Processing => "processing",
        OrderStatus.Shipped    => "shipped",
        OrderStatus.Delivered  => "delivered",
        OrderStatus.Cancelled  => "cancelled",
        OrderStatus.Refunded   => "refunded",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    public static OrderStatus ParseOrderStatus(string value) => value switch
    {
        "pending"    => OrderStatus.Pending,
        "paid"       => OrderStatus.Paid,
        "processing" => OrderStatus.Processing,
        "shipped"    => OrderStatus.Shipped,
        "delivered"  => OrderStatus.Delivered,
        "cancelled"  => OrderStatus.Cancelled,
        "refunded"   => OrderStatus.Refunded,
        _ => throw new ArgumentException($"Unknown order status: {value}", nameof(value))
    };

    public static string ToDbString(this PaymentStatus s) => s switch
    {
        PaymentStatus.Pending   => "pending",
        PaymentStatus.Succeeded => "succeeded",
        PaymentStatus.Failed    => "failed",
        PaymentStatus.Refunded  => "refunded",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    public static PaymentStatus ParsePaymentStatus(string value) => value switch
    {
        "pending"   => PaymentStatus.Pending,
        "succeeded" => PaymentStatus.Succeeded,
        "failed"    => PaymentStatus.Failed,
        "refunded"  => PaymentStatus.Refunded,
        _ => throw new ArgumentException($"Unknown payment status: {value}", nameof(value))
    };
}
