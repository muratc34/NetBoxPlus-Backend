using System.Collections.Concurrent;
using Billing.Application.Abstractions;
using Shared.Domain;

namespace Billing.IntegrationTests;

/// <summary>Records what the API asks the provider, and answers like it would, or with a set error.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public ConcurrentQueue<Guid> CustomersCreated { get; } = new();

    public ConcurrentQueue<CheckoutRequest> Checkouts { get; } = new();

    public ConcurrentQueue<string> Portals { get; } = new();

    /// <summary>When set, every call fails with it.</summary>
    public Error? FailWith { get; set; }

    /// <summary>When set, checkouts wait for it: holds a request open.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource CheckoutStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reset()
    {
        FailWith = null;
        Gate = null;
        CheckoutStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<Result<string>> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        if (FailWith is { } error)
        {
            return Task.FromResult<Result<string>>(error);
        }

        CustomersCreated.Enqueue(userId);
        return Task.FromResult<Result<string>>($"cus_{userId:N}");
    }

    public async Task<Result<Uri>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        CheckoutStarted.TrySetResult();
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (FailWith is { } error)
        {
            return error;
        }

        Checkouts.Enqueue(request);
        return new Uri($"https://checkout.test/{request.UserId:N}/{Uri.EscapeDataString(request.IdempotencyKey)}/{Guid.NewGuid():N}");
    }

    public Task<Result<Uri>> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken)
    {
        if (FailWith is { } error)
        {
            return Task.FromResult<Result<Uri>>(error);
        }

        Portals.Enqueue(providerCustomerId);
        return Task.FromResult<Result<Uri>>(new Uri($"https://portal.test/{providerCustomerId}"));
    }
}
