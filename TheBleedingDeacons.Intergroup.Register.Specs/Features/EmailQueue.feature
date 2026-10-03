Feature: The email queue
  As the volunteer on the door
  I want emails to wait patiently when they cannot go
  So that a bad connection at the venue loses nobody their confirmation

  Welcome and consent emails are queued, never sent while someone waits.
  The queue is worked through in the background, and an email is tried a
  limited number of times before it is marked as failed.

  Background:
    Given the group "Monday Step"
    And "Ann B" is a GSR of "Monday Step" with the email "ann@example.org"
    And "Monday Step" has been registered

  Scenario: A queued email is sent when the queue runs
    When the queue runs
    Then the email to "ann@example.org" has been sent

  Scenario: Offline, the queue waits and nothing reaches the mail server
    Given the tablet is offline
    When the queue runs
    Then the email to "ann@example.org" is waiting
    And nothing reached the mail server

  # Found, not chosen. The connection is made once for the whole batch, and
  # a refused password fails it before any email is tried — so no attempt
  # is counted, and the run reports itself the way an offline run does.
  # The background loop reads that as "offline" too, which is why a wrong
  # password can never trip the circuit breaker. See specs/domain-model.md.
  Scenario: A refused password leaves the email waiting, untried
    Given the mail server refuses the password
    When the queue runs
    Then the email to "ann@example.org" is waiting, after 0 attempts
    And the queue run reported that it did not run

  Scenario: The last allowed attempt marks the email failed
    Given the email to "ann@example.org" has already failed twice
    And the mail server refuses every message
    When the queue runs
    Then the email to "ann@example.org" has failed
