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

  # The connection is made once for the whole batch, and a refused password
  # fails it before any email is tried, so no attempt is counted against the
  # email. It is the password that is wrong, not the email.
  Scenario: A refused password leaves the email waiting, untried
    Given the mail server refuses the password
    When the queue runs
    Then the email to "ann@example.org" is waiting, after 0 attempts
    And the queue run reported that it did not run

  # Until register#53 a refused password could never pause anything: the run
  # reported itself the way an offline run does, and the background read
  # that as "offline". Now the background sees the refusal itself.
  Scenario: Three refused passwords in a row pause background sending
    Given the mail server refuses the password
    When the queue runs in the background 3 times
    Then background sending is paused
    And the email to "ann@example.org" is waiting, after 0 attempts

  # A server the tablet cannot reach is the venue's network, not the
  # tablet's settings. Pausing for it would hold emails back after the
  # network came back.
  Scenario: An unreachable mail server never pauses background sending
    Given the mail server cannot be reached
    When the queue runs in the background 3 times
    Then background sending is not paused

  Scenario: Offline, background sending is never paused
    Given the tablet is offline
    When the queue runs in the background 3 times
    Then background sending is not paused
    And nothing reached the mail server

  Scenario: The last allowed attempt marks the email failed
    Given the email to "ann@example.org" has already failed twice
    And the mail server refuses every message
    When the queue runs
    Then the email to "ann@example.org" has failed
