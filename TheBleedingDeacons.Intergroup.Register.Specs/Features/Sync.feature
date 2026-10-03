Feature: Loading the intergroup's records
  As the volunteer setting up before a meeting
  I want the tablet to hold exactly what Unity holds
  So that the people on the door are working from the real list

  A sync fetches groups, positions, members and intergroup meetings from
  Unity, every page of each, and then replaces what the tablet held in one
  transaction. Replaces, not merges: whatever the tablet had is gone.

  Scenario: Every page is fetched
    Given Unity holds the group "Monday Step"
    And Unity holds 5 members of "Monday Step"
    And Unity serves 2 to a page
    When the tablet syncs
    Then the tablet holds 5 members
    And Unity was asked for 3 pages of members

  Scenario: What the tablet held is replaced, not merged
    Given the group "Old Group"
    And "Ann B" is a GSR of "Old Group"
    And "Old Group" has been registered
    And Unity holds the group "Monday Step"
    When the tablet syncs
    Then the tablet does not hold the group "Old Group"
    And the tablet holds 0 members

  Scenario: A member whose home group is outside what was fetched keeps their place, without the group
    Given Unity holds the group "Monday Step"
    And Unity holds the member "Ann B" whose home group is not in the district
    When the tablet syncs
    Then the tablet holds 1 member
    And "Ann B" has no home group

  Scenario: A page Unity refuses fails the sync, and the tablet keeps what it had
    Given the group "Old Group"
    And Unity holds the group "Monday Step"
    And Unity holds 5 members of "Monday Step"
    And Unity serves 2 to a page
    And Unity refuses page 2
    When the tablet syncs
    Then the sync failed
    And the tablet still holds the group "Old Group"
